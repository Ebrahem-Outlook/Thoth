using Microsoft.Extensions.Options;
using Thoth.Core.Agent;
using Thoth.Core.Configuration;
using Thoth.Core.Memory;
using Thoth.Core.Tools;
using Thoth.Runtime;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile(Path.Combine(Environment.CurrentDirectory, "configs", "thoth.json"), optional: true)
    .AddEnvironmentVariables("THOTH_");

builder.Services.AddThothRuntime(builder.Configuration);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    service = "Thoth.Api",
    status = "ok",
    time = DateTimeOffset.UtcNow
}));

app.MapGet("/tools", (IToolRegistry registry) =>
{
    var tools = registry.List().Select(tool => new
    {
        tool.Name,
        tool.Description,
        Parameters = tool.Parameters
    });

    return Results.Ok(tools);
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

app.MapGet("/memory/search", async (
    string query,
    string? scope,
    int? limit,
    IMemoryStore memory,
    CancellationToken cancellationToken) =>
{
    var records = await memory.SearchAsync(query, scope, limit ?? 8, cancellationToken);
    return Results.Ok(records);
});

app.Run();

public sealed record RunRequest(
    string Goal,
    string? Workspace,
    string? Model,
    int? MaxSteps,
    bool? DryRun);
