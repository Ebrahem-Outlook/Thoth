using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Thoth.Core.Chat;
using Thoth.Core.Memory;
using Thoth.Core.Planning;
using Thoth.Core.Sandbox;
using Thoth.Core.Tools;

namespace Thoth.Core.Agent;

public sealed class AgentEngine(
    IChatModel chatModel,
    IAgentPlanner planner,
    IToolRegistry tools,
    IMemoryStore memory,
    IExecutionPolicy policy,
    ILogger<AgentEngine>? logger = null)
{
    private readonly ILogger<AgentEngine> logger = logger ?? NullLogger<AgentEngine>.Instance;

    public async Task<AgentRun> RunAsync(AgentRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Goal))
        {
            throw new ArgumentException("Agent goal cannot be empty.", nameof(request));
        }

        var runId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var workingDirectory = Path.GetFullPath(request.WorkingDirectory);

        if (!Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException($"Working directory does not exist: {workingDirectory}");
        }

        await memory.EnsureCreatedAsync(cancellationToken);
        var relevantMemories = (await memory.SearchAsync(request.Goal, "project", limit: 6, cancellationToken: cancellationToken))
            .Where(record => !string.Equals(record.Scope, "run", StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .ToArray();

        var planningContext = new AgentPlanningContext(request, relevantMemories, tools.List());
        var plan = await planner.CreatePlanAsync(planningContext, cancellationToken);
        var toolContext = new ToolContext(workingDirectory, memory, policy, request.DryRun);
        var executedSteps = new List<AgentStep>();

        foreach (var planStep in plan.Steps.Take(Math.Max(request.MaxSteps, 0)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stepStartedAt = DateTimeOffset.UtcNow;
            ToolResult? result = null;

            if (planStep.Invocation is not null)
            {
                result = await ExecuteToolAsync(planStep.Invocation, toolContext, cancellationToken);
            }

            executedSteps.Add(new AgentStep(
                executedSteps.Count + 1,
                planStep.Thought,
                planStep.Invocation,
                result,
                stepStartedAt,
                DateTimeOffset.UtcNow));
        }

        var finalAnswer = await SynthesizeFinalAnswerAsync(request, plan, executedSteps, cancellationToken);
        var succeeded = executedSteps.All(step => step.Result is null || step.Result.Succeeded);

        await memory.AddAsync(
            "project",
            BuildProjectMemory(request.Goal, finalAnswer),
            new Dictionary<string, string>
            {
                ["runId"] = runId.ToString("N"),
                ["succeeded"] = succeeded.ToString(),
                ["kind"] = "agent_outcome"
            },
            cancellationToken);

        return new AgentRun(
            runId,
            request.Goal,
            workingDirectory,
            plan,
            executedSteps,
            finalAnswer,
            succeeded,
            startedAt,
            DateTimeOffset.UtcNow);
    }

    private async Task<ToolResult> ExecuteToolAsync(
        ToolInvocation invocation,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        var tool = tools.Find(invocation.ToolName);
        if (tool is null)
        {
            return ToolResult.Failure(invocation.ToolName, $"Unknown tool: {invocation.ToolName}");
        }

        var decision = policy.Authorize(invocation, context);
        if (!decision.Allowed)
        {
            return ToolResult.Failure(invocation.ToolName, $"Policy denied tool call: {decision.Reason}");
        }

        try
        {
            logger.LogInformation("Running tool {ToolName}", invocation.ToolName);
            return await tool.InvokeAsync(invocation, context, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tool {ToolName} failed", invocation.ToolName);
            return ToolResult.Failure(invocation.ToolName, ex.Message);
        }
    }

    private async Task<string> SynthesizeFinalAnswerAsync(
        AgentRequest request,
        AgentPlan plan,
        IReadOnlyList<AgentStep> steps,
        CancellationToken cancellationToken)
    {
        var observations = new StringBuilder();
        foreach (var step in steps)
        {
            observations.AppendLine($"Step {step.Index}: {step.Thought}");
            if (step.Invocation is not null)
            {
                observations.AppendLine($"Tool: {step.Invocation.ToolName}");
            }

            if (step.Result is not null)
            {
                observations.AppendLine($"Succeeded: {step.Result.Succeeded}");
                observations.AppendLine(Trim(step.Result.Content, 2000));
            }

            observations.AppendLine();
        }

        var response = await chatModel.CompleteAsync(
            new ChatRequest(
                [
                    new ChatMessage(ChatRole.System, "You are Thoth, a precise AI agent. Summarize what happened and the next best action."),
                    new ChatMessage(ChatRole.User, $"""
                    Goal:
                    {request.Goal}

                    Plan:
                    {plan.Summary}

                    Observations:
                    {observations}

                    Write a direct, useful final answer.
                    """)
                ],
                request.Model,
                0.2),
            cancellationToken);

        return string.IsNullOrWhiteSpace(response.Content)
            ? "The agent completed its run but produced no final message."
            : response.Content.Trim();
    }

    private static string Trim(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "\n[truncated]";
    }

    private static string BuildProjectMemory(
        string goal,
        string finalAnswer)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Task: {Trim(SingleLine(goal), 220)}");
        builder.AppendLine($"Outcome: {Trim(BuildOutcomeSummary(finalAnswer), 520)}");

        return builder.ToString().Trim();
    }

    private static string BuildOutcomeSummary(string finalAnswer)
    {
        var lines = finalAnswer
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var summary = new List<string>();
        var skipNextIntentBullet = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("Tools used:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Next best move:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Step ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Tool:", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (line.StartsWith("Thoth self-run complete", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("I inspected the workspace", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Arabic request detected", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.StartsWith("Intent understood:", StringComparison.OrdinalIgnoreCase))
            {
                skipNextIntentBullet = true;
                continue;
            }

            if (skipNextIntentBullet && line.StartsWith("- ", StringComparison.Ordinal))
            {
                skipNextIntentBullet = false;
                continue;
            }

            skipNextIntentBullet = false;
            summary.Add(line);
        }

        return SingleLine(string.Join(" ", summary.Take(6)));
    }

    private static string SingleLine(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
