using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Thoth.Api.Contracts;
using Thoth.Api.Services;
using Thoth.Core.Agent;
using Thoth.Core.Configuration;
using Thoth.Core.Conversations;
using Thoth.Core.Memory;
using Thoth.Core.Tools;
using Thoth.Model.Persistence;
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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Thoth API",
        Version = "v1",
        Description = "HTTP API for Thoth's local neural agent runtime, conversations, memory, tools, attachments, and workspace inspection."
    });
});

builder.Services.AddThothRuntime(builder.Configuration);
builder.Services.AddSingleton<AttachmentStorageService>();
builder.Services.AddSingleton<WorkspaceInspectionService>();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.DocumentTitle = "Thoth API";
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Thoth API v1");
    options.RoutePrefix = "swagger";
});

app.UseCors("ThothWeb");

app.MapGet("/", () => Results.Redirect("/swagger"))
    .ExcludeFromDescription();

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
        "streaming",
        "workspace_summary",
        "iterative_agent",
        "neural_training",
        "checkpoint_inference",
        "web_search",
        "web_read",
        "web_research"
    }
}));

app.MapGet("/api/system/status", async (
    IOptions<ThothOptions> options,
    IConversationStore conversations,
    IMemoryStore memory,
    IToolRegistry tools,
    CancellationToken cancellationToken) =>
{
    var conversationCount = (await conversations.ListAsync(limit: 500, cancellationToken: cancellationToken)).Count;
    var memoryCount = (await memory.RecentAsync(limit: 500, cancellationToken: cancellationToken)).Count;
    var modelStatus = await InspectModelAsync(options.Value, cancellationToken);

    return Results.Ok(new SystemStatus(
        options.Value.Model.Provider,
        options.Value.Model.Model,
        modelStatus.Status != ModelCheckpointStatus.QualifiedForGeneration &&
        modelStatus.Status != ModelCheckpointStatus.QualifiedForUnderstanding &&
        modelStatus.Status != ModelCheckpointStatus.QualifiedForAgentDecisions,
        options.Value.Sandbox.AllowShell,
        tools.List().Count,
        conversationCount,
        memoryCount,
        DateTimeOffset.UtcNow,
        modelStatus.Status.ToString(),
        modelStatus.Reasons));
});

app.MapGet("/api/model/status", async (
    IOptions<ThothOptions> options,
    CancellationToken cancellationToken) =>
{
    var status = await InspectModelAsync(options.Value, cancellationToken);
    return Results.Ok(new
    {
        status = status.Status.ToString(),
        status.CheckpointPath,
        status.MetadataPath,
        status.Reasons,
        metadata = status.Metadata
    });
});

app.MapGet("/api/workspace/summary", (
    IOptions<ThothOptions> options,
    WorkspaceInspectionService inspector) =>
{
    return Results.Ok(inspector.Inspect(options.Value.WorkspaceRoot));
});

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
    var resolvedScope = string.IsNullOrWhiteSpace(scope) ? "project" : scope;
    var records = string.IsNullOrWhiteSpace(query)
        ? await memory.RecentAsync(resolvedScope, limit ?? 8, cancellationToken)
        : await memory.SearchAsync(query, resolvedScope, limit ?? 8, cancellationToken);

    return Results.Ok(records
        .Where(record => !record.Scope.Equals("run", StringComparison.OrdinalIgnoreCase))
        .Select(SanitizeMemoryRecord)
        .GroupBy(MemoryDedupeKey, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .ToArray());
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

static Task<ModelCheckpointInspection> InspectModelAsync(
    ThothOptions options,
    CancellationToken cancellationToken) =>
    ModelCheckpointQualityGate.InspectAsync(
        options.Model.CheckpointPath,
        new CheckpointQualityThresholds(
            options.Model.Quality.MinimumOptimizerSteps,
            options.Model.Quality.MinimumEvaluatedTokens,
            options.Model.Quality.MaximumAverageLoss,
            options.Model.Quality.MaximumPerplexity,
            options.Model.Quality.MinimumGenerationHealthScore,
            options.Model.Quality.MinimumUnderstandingScore,
            options.Model.Quality.MinimumAgentDecisionScore),
        cancellationToken);

static ChatResponseDto ToDto(ChatTurnResult result) =>
    new(
        result.Conversation.Conversation.Id,
        result.UserMessage.Id,
        result.AssistantMessage.Id,
        result.AssistantMessage.Content,
        result.Understanding,
        result.AgentRun,
        new DeveloperDiagnosticsDto(
            result.Understanding,
            result.AgentRun,
            result.AgentRun?.Steps.Count > 0,
            result.AgentRun?.Plan.Source));

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

static MemoryRecord SanitizeMemoryRecord(MemoryRecord record) =>
    record with { Content = SanitizeMemoryContent(record.Content) };

static string MemoryDedupeKey(MemoryRecord record)
{
    var task = ExtractUntilFirstLabel(record.Content, "Task:", "Outcome:");
    return string.IsNullOrWhiteSpace(task) ? record.Content : task;
}

static string SanitizeMemoryContent(string content)
{
    var normalized = NormalizeMemoryText(content);
    var resultIndex = normalized.IndexOf("Result:", StringComparison.OrdinalIgnoreCase);
    var outcomeIndex = normalized.IndexOf("Outcome:", StringComparison.OrdinalIgnoreCase);
    var resultComesFirst = resultIndex >= 0 && (outcomeIndex < 0 || resultIndex < outcomeIndex);

    var task = ExtractUntilFirstLabel(normalized, "Task:", "Result:", "Outcome:", "Understanding:");
    var outcome = resultComesFirst
        ? ExtractAfter(normalized, "Result:")
        : ExtractAfter(normalized, "Outcome:");

    if (string.IsNullOrWhiteSpace(outcome))
    {
        task = ExtractBetween(normalized, "Task:", "Result:");
        outcome = ExtractAfter(normalized, "Result:");
    }

    if (string.IsNullOrWhiteSpace(task))
    {
        task = ExtractUntilFirstLabel(normalized, "Goal:", "Result:", "Outcome:", "Understanding:");
    }

    if (string.IsNullOrWhiteSpace(outcome))
    {
        outcome = ExtractAfter(normalized, "Result:");
    }

    if (!string.IsNullOrWhiteSpace(outcome))
    {
        var readable = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(task))
        {
            readable.Append("Task: ");
            readable.Append(ClampMemoryText(CleanMemoryFragment(task), 180));
            readable.Append(' ');
        }

        readable.Append("Outcome: ");
        readable.Append(ClampMemoryText(CleanMemoryOutcome(outcome), 340));
        return ClampMemoryText(readable.ToString(), 560);
    }

    return ClampMemoryText(CleanMemoryOutcome(normalized), 420);
}

static string NormalizeMemoryText(string value)
{
    var decoded = Regex.Replace(value, @"\\u(?<hex>[0-9a-fA-F]{4})", match =>
    {
        var codePoint = Convert.ToInt32(match.Groups["hex"].Value, 16);
        return ((char)codePoint).ToString();
    });

    return string.Join(' ', decoded.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
}

static string CleanMemoryOutcome(string value)
{
    var normalized = CleanMemoryFragment(value);
    foreach (var label in new[] { "Backend findings:", "Frontend findings:", "Key findings:" })
    {
        var section = ExtractSection(normalized, label, "Tools used:", "Next best move:", "Step ", "Tool:");
        if (!string.IsNullOrWhiteSpace(section))
        {
            var cleanSection = CleanMemoryFragment(section);
            if (label.Equals("Key findings:", StringComparison.OrdinalIgnoreCase))
            {
                var infrastructureIndex = FirstIndexOf(
                    cleanSection,
                    " - Workspace:",
                    " - Files:",
                    " - Directories:",
                    " - Projects:");
                if (infrastructureIndex >= 0)
                {
                    cleanSection = cleanSection[..infrastructureIndex].Trim();
                }
            }

            return $"{label} {cleanSection}";
        }
    }

    var stopIndex = FirstIndexOf(normalized, "Tools used:", "Next best move:", "Step ", "Tool:");
    if (stopIndex >= 0)
    {
        normalized = normalized[..stopIndex].Trim();
    }

    normalized = normalized
        .Replace("Thoth self-run complete.", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Replace("I inspected the workspace with Thoth's local tools.", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Replace("Intent understood:", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Trim();

    return CleanMemoryFragment(normalized);
}

static string CleanMemoryFragment(string value)
{
    var withoutLocalPaths = Regex.Replace(value, @"[A-Za-z]:\\[^\s]+", "[workspace]");
    return string.Join(' ', withoutLocalPaths.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '-', ':');
}

static string ClampMemoryText(string value, int maxLength) =>
    value.Length <= maxLength ? value : value[..maxLength].TrimEnd() + "...";

static string ExtractSection(string value, string label, params string[] endLabels)
{
    var section = ExtractAfter(value, label);
    if (string.IsNullOrWhiteSpace(section))
    {
        return string.Empty;
    }

    var endIndex = FirstIndexOf(section, endLabels);
    return endIndex < 0 ? section.Trim() : section[..endIndex].Trim();
}

static string ExtractUntilFirstLabel(string value, string start, params string[] endLabels)
{
    var startIndex = value.IndexOf(start, StringComparison.OrdinalIgnoreCase);
    if (startIndex < 0)
    {
        return string.Empty;
    }

    startIndex += start.Length;
    var rest = value[startIndex..];
    var endIndex = FirstIndexOf(rest, endLabels);
    return endIndex < 0 ? rest.Trim() : rest[..endIndex].Trim();
}

static int FirstIndexOf(string value, params string[] needles)
{
    var indexes = needles
        .Select(needle => value.IndexOf(needle, StringComparison.OrdinalIgnoreCase))
        .Where(index => index >= 0)
        .ToArray();

    return indexes.Length == 0 ? -1 : indexes.Min();
}

static string ExtractBetween(string value, string start, string end)
{
    var startIndex = value.IndexOf(start, StringComparison.OrdinalIgnoreCase);
    if (startIndex < 0)
    {
        return string.Empty;
    }

    startIndex += start.Length;
    var endIndex = value.IndexOf(end, startIndex, StringComparison.OrdinalIgnoreCase);
    return endIndex < 0 ? value[startIndex..].Trim() : value[startIndex..endIndex].Trim();
}

static string ExtractAfter(string value, string label)
{
    var index = value.IndexOf(label, StringComparison.OrdinalIgnoreCase);
    return index < 0 ? string.Empty : value[(index + label.Length)..].Trim();
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
