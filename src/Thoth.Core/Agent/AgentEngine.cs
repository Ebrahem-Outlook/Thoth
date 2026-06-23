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
        var relevantMemories = await memory.SearchAsync(request.Goal, limit: 6, cancellationToken: cancellationToken);

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

                await memory.AddAsync(
                    "run",
                    $"Run {runId}: {planStep.Invocation.ToolName} => {(result.Succeeded ? "ok" : "failed")}: {Trim(result.Content, 500)}",
                    new Dictionary<string, string>
                    {
                        ["runId"] = runId.ToString("N"),
                        ["tool"] = planStep.Invocation.ToolName,
                        ["succeeded"] = result.Succeeded.ToString()
                    },
                    cancellationToken);
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
            $"Goal: {request.Goal}\nResult: {Trim(finalAnswer, 1200)}",
            new Dictionary<string, string>
            {
                ["runId"] = runId.ToString("N"),
                ["succeeded"] = succeeded.ToString()
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
}
