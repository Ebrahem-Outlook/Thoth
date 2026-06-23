using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Thoth.Api.Contracts;
using Thoth.Api.Services;
using Thoth.Core.Agent;
using Thoth.Core.Configuration;
using Thoth.Core.Conversations;
using Thoth.Core.Memory;
using Thoth.Core.Tools;
using Thoth.Runtime;

var builder = WebApplication.CreateBuilder(args);
var workspaceRoot = ThothPathDiscovery.FindWorkspaceRoot(Environment.CurrentDirectory);

builder.Configuration
    .AddJsonFile(Path.Combine(workspaceRoot, "configs", "thoth.json"), optional: true)
    .AddEnvironmentVariables("THOTH_");

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 100 * 1024 * 1024;
    options.ValueLengthLimit = 4 * 1024 * 1024;
    options.MultipartHeadersLengthLimit = 64 * 1024;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("ThothWeb", policy => policy
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()
        .SetIsOriginAllowed(origin =>
            origin.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase) ||
            origin.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase)));
});

builder.Services.AddThothRuntime(builder.Configuration);
builder.Services.AddSingleton<AttachmentStorageService>();

var app = builder.Build();
app.UseCors("ThothWeb");

app.MapGet("/health", () => Results.Ok(new
{
    service = "Thoth.Api",
    status = "ok",
    time = DateTimeOffset.UtcNow
}));

app.MapGet("/api/client-config", (IOptions<ThothOptions> options) => Results.Ok(new
{
    options.Value.Model.Provider,
    options.Value.Model.Model,
    options.Value.Sandbox.AllowShell,
    options.Value.MaxAgentSteps,
    features = new[]
    {
        "conversations",
        "attachments",
        "images",
        "memory",
        "tools",
        "streaming"
    }
}));

app.MapGet("/api/tools", (IToolRegistry registry) =>
{
    var tools = registry.List().Select(tool => new
    {
        tool.Name,
        tool.Description,
        Parameters = tool.Parameters
    });

    return Results.Ok(tools);
});

app.MapGet("/api/conversations", async (
    string? query,
    string? project,
    bool? includeArchived,
    int? limit,
    IConversationStore store,
    CancellationToken cancellationToken) =>
{
    var conversations = await store.ListAsync(query, project, includeArchived ?? false, limit ?? 100, cancellationToken);
    return Results.Ok(conversations);
});

app.MapPost("/api/conversations", async (
    CreateConversationRequest request,
    IConversationStore store,
    CancellationToken cancellationToken) =>
{
    var conversation = await store.CreateAsync(request.Title ?? "New chat", request.Project, cancellationToken);
    return Results.Created($"/api/conversations/{conversation.Id}", conversation);
});

app.MapGet("/api/conversations/{conversationId:guid}", async (
    Guid conversationId,
    IConversationStore store,
    CancellationToken cancellationToken) =>
{
    var conversation = await store.GetAsync(conversationId, cancellationToken);
    return conversation is null ? Results.NotFound() : Results.Ok(conversation);
});

app.MapPatch("/api/conversations/{conversationId:guid}", async (
    Guid conversationId,
    UpdateConversationRequest request,
    IConversationStore store,
    CancellationToken cancellationToken) =>
{
    var updated = await store.UpdateAsync(
        conversationId,
        request.Title,
        request.IsPinned,
        request.IsArchived,
        request.Project,
        cancellationToken);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

app.MapDelete("/api/conversations/{conversationId:guid}", async (
    Guid conversationId,
    IConversationStore store,
    CancellationToken cancellationToken) =>
{
    return await store.DeleteAsync(conversationId, cancellationToken)
        ? Results.NoContent()
        : Results.NotFound();
});

app.MapPost("/api/chat", async (
    HttpRequest httpRequest,
    ChatOrchestrator orchestrator,
    AttachmentStorageService attachments,
    IOptions<ThothOptions> options,
    CancellationToken cancellationToken) =>
{
    var parsed = await ParseMultipartChatAsync(httpRequest, attachments, null, cancellationToken);
    var result = await orchestrator.SendAsync(ToChatTurnRequest(parsed, options.Value), cancellationToken);
    return Results.Ok(ToDto(result));
});

app.MapPost("/api/conversations/{conversationId:guid}/messages", async (
    Guid conversationId,
    HttpRequest httpRequest,
    ChatOrchestrator orchestrator,
    AttachmentStorageService attachments,
    IOptions<ThothOptions> options,
    CancellationToken cancellationToken) =>
{
    var parsed = await ParseMultipartChatAsync(httpRequest, attachments, conversationId, cancellationToken);
    parsed = parsed with { ConversationId = conversationId };
    var result = await orchestrator.SendAsync(ToChatTurnRequest(parsed, options.Value), cancellationToken);
    return Results.Ok(ToDto(result));
});

app.MapPost("/api/conversations/{conversationId:guid}/messages/stream", async (
    Guid conversationId,
    StreamChatRequest request,
    ChatOrchestrator orchestrator,
    IOptions<ThothOptions> options,
    HttpResponse response,
    CancellationToken cancellationToken) =>
{
    response.Headers.ContentType = "text/event-stream";
    response.Headers.CacheControl = "no-cache";

    var turn = await orchestrator.SendAsync(
        new ChatTurnRequest(
            conversationId,
            request.Content,
            request.AttachmentIds ?? [],
            options.Value.WorkspaceRoot,
            request.Model ?? options.Value.Model.Model,
            request.UseTools ?? true,
            request.MaxSteps ?? options.Value.MaxAgentSteps),
        cancellationToken);

    await WriteSseAsync(response, "meta", new
    {
        conversationId = turn.Conversation.Conversation.Id,
        userMessageId = turn.UserMessage.Id,
        assistantMessageId = turn.AssistantMessage.Id,
        turn.Understanding
    }, cancellationToken);

    foreach (var chunk in Chunk(turn.AssistantMessage.Content, 80))
    {
        await WriteSseAsync(response, "delta", new { text = chunk }, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    await WriteSseAsync(response, "done", ToDto(turn), cancellationToken);
});

app.MapPost("/api/attachments", async (
    HttpRequest request,
    AttachmentStorageService storage,
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "multipart/form-data is required." });
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var uploaded = new List<ConversationAttachment>();
    foreach (var file in form.Files)
    {
        uploaded.Add(await storage.SaveAsync(file, cancellationToken: cancellationToken));
    }

    return Results.Ok(uploaded);
});

app.MapGet("/api/attachments/{attachmentId:guid}/download", async (
    Guid attachmentId,
    IConversationStore store,
    CancellationToken cancellationToken) =>
{
    var attachment = await store.GetAttachmentAsync(attachmentId, cancellationToken);
    if (attachment is null || !File.Exists(attachment.StoragePath))
    {
        return Results.NotFound();
    }

    return Results.File(attachment.StoragePath, attachment.ContentType, attachment.FileName);
});

app.MapGet("/api/memory/search", async (
    string query,
    string? scope,
    int? limit,
    IMemoryStore memory,
    CancellationToken cancellationToken) =>
{
    var records = await memory.SearchAsync(query, scope, limit ?? 8, cancellationToken);
    return Results.Ok(records);
});

app.MapPost("/api/memory", async (
    MemoryWriteRequest request,
    IMemoryStore memory,
    CancellationToken cancellationToken) =>
{
    var record = await memory.AddAsync(request.Scope ?? "project", request.Content, cancellationToken: cancellationToken);
    return Results.Created($"/api/memory/{record.Id}", record);
});

app.MapPost("/runs", async (
    RunRequest request,
    AgentEngine engine,
    IOptions<ThothOptions> options,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Goal))
    {
        return Results.BadRequest(new { error = "Goal is required." });
    }

    var value = options.Value;
    var run = await engine.RunAsync(
        new AgentRequest(
            request.Goal,
            request.Workspace ?? value.WorkspaceRoot,
            request.Model ?? value.Model.Model,
            request.MaxSteps ?? value.MaxAgentSteps,
            request.DryRun ?? false),
        cancellationToken);

    return Results.Ok(run);
});

app.Run();

static ChatTurnRequest ToChatTurnRequest(ParsedChatRequest parsed, ThothOptions options) =>
    new(
        parsed.ConversationId,
        parsed.Content,
        parsed.AttachmentIds,
        options.WorkspaceRoot,
        parsed.Model ?? options.Model.Model,
        parsed.UseTools ?? true,
        parsed.MaxSteps ?? options.MaxAgentSteps);

static ChatResponseDto ToDto(ChatTurnResult result) =>
    new(
        result.Conversation.Conversation.Id,
        result.UserMessage.Id,
        result.AssistantMessage.Id,
        result.AssistantMessage.Content,
        result.Understanding,
        result.AgentRun);

static async Task<ParsedChatRequest> ParseMultipartChatAsync(
    HttpRequest request,
    AttachmentStorageService attachments,
    Guid? conversationId,
    CancellationToken cancellationToken)
{
    if (!request.HasFormContentType)
    {
        throw new InvalidOperationException("multipart/form-data is required.");
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var content = form["content"].ToString();
    if (string.IsNullOrWhiteSpace(content) && form.Files.Count == 0)
    {
        throw new InvalidOperationException("Message content or at least one attachment is required.");
    }

    var attachmentIds = new List<Guid>();
    foreach (var file in form.Files)
    {
        var saved = await attachments.SaveAsync(file, conversationId, cancellationToken);
        attachmentIds.Add(saved.Id);
    }

    if (Guid.TryParse(form["conversationId"], out var parsedConversationId))
    {
        conversationId = parsedConversationId;
    }

    return new ParsedChatRequest(
        conversationId,
        content,
        attachmentIds,
        EmptyToNull(form["model"].ToString()),
        TryBool(form["useTools"].ToString()),
        TryInt(form["maxSteps"].ToString()));
}

static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

static bool? TryBool(string value) => bool.TryParse(value, out var parsed) ? parsed : null;

static int? TryInt(string value) => int.TryParse(value, out var parsed) ? parsed : null;

static async Task WriteSseAsync(HttpResponse response, string eventName, object payload, CancellationToken cancellationToken)
{
    var json = System.Text.Json.JsonSerializer.Serialize(payload);
    await response.WriteAsync($"event: {eventName}\n", Encoding.UTF8, cancellationToken);
    await response.WriteAsync($"data: {json}\n\n", Encoding.UTF8, cancellationToken);
}

static IEnumerable<string> Chunk(string text, int size)
{
    for (var index = 0; index < text.Length; index += size)
    {
        yield return text[index..Math.Min(index + size, text.Length)];
    }
}

public sealed record RunRequest(
    string Goal,
    string? Workspace,
    string? Model,
    int? MaxSteps,
    bool? DryRun);

public sealed record MemoryWriteRequest(string Content, string? Scope);

public sealed record ParsedChatRequest(
    Guid? ConversationId,
    string Content,
    IReadOnlyList<Guid> AttachmentIds,
    string? Model,
    bool? UseTools,
    int? MaxSteps);
