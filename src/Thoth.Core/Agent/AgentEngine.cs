using System.Text;
using System.Text.Json;
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
    IAgentDecisionService decisions,
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
        var relevantMemories = (await memory.SearchAsync(
                request.Goal,
                "project",
                limit: 6,
                cancellationToken: cancellationToken))
            .Where(record => !string.Equals(record.Scope, "run", StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .ToArray();

        var normalizedRequest = request with { WorkingDirectory = workingDirectory };
        var toolContext = new ToolContext(workingDirectory, memory, policy, request.DryRun);
        var executedSteps = new List<AgentStep>();
        var planSteps = new List<AgentPlanStep>();
        var callCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        string? finalAnswer = null;
        var stopReason = string.Empty;

        for (var index = 0; index < Math.Max(request.MaxSteps, 0); index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decision = await decisions.DecideAsync(
                new AgentDecisionContext(normalizedRequest, relevantMemories, tools.List(), executedSteps),
                cancellationToken);

            if (decision.Kind == AgentDecisionKind.Final)
            {
                finalAnswer = decision.Answer;
                stopReason = decision.Rationale;
                break;
            }

            if (decision.Kind == AgentDecisionKind.Stop || decision.Invocation is null)
            {
                stopReason = decision.Rationale;
                break;
            }

            var fingerprint = BuildFingerprint(decision.Invocation);
            callCounts.TryGetValue(fingerprint, out var repeats);
            repeats++;
            callCounts[fingerprint] = repeats;
            if (repeats > 2)
            {
                stopReason = $"Stopped a repeated tool loop for {decision.Invocation.ToolName}.";
                break;
            }

            var stepStartedAt = DateTimeOffset.UtcNow;
            var result = await ExecuteToolAsync(decision.Invocation, toolContext, cancellationToken);
            var step = new AgentStep(
                executedSteps.Count + 1,
                decision.Rationale,
                decision.Invocation,
                result,
                stepStartedAt,
                DateTimeOffset.UtcNow);
            executedSteps.Add(step);
            planSteps.Add(new AgentPlanStep(decision.Rationale, decision.Invocation));
        }

        if (string.IsNullOrWhiteSpace(finalAnswer))
        {
            finalAnswer = await SynthesizeFinalAnswerAsync(
                normalizedRequest,
                executedSteps,
                stopReason,
                cancellationToken);
        }

        var plan = new AgentPlan(
            "Iterative observe-decide-act execution. Each action was chosen after the previous observation.",
            planSteps,
            "iterative-decision-loop");
        var succeeded = executedSteps.All(step => step.Result is null || step.Result.Succeeded) &&
                        !string.IsNullOrWhiteSpace(finalAnswer);

        await memory.AddAsync(
            "project",
            BuildProjectMemory(request.Goal, finalAnswer),
            new Dictionary<string, string>
            {
                ["runId"] = runId.ToString("N"),
                ["succeeded"] = succeeded.ToString(),
                ["kind"] = "agent_outcome",
                ["steps"] = executedSteps.Count.ToString()
            },
            cancellationToken);

        return new AgentRun(
            runId,
            request.Goal,
            workingDirectory,
            plan,
            executedSteps,
            finalAnswer.Trim(),
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Tool {ToolName} failed", invocation.ToolName);
            return ToolResult.Failure(invocation.ToolName, exception.Message);
        }
    }

    private async Task<string> SynthesizeFinalAnswerAsync(
        AgentRequest request,
        IReadOnlyList<AgentStep> steps,
        string stopReason,
        CancellationToken cancellationToken)
    {
        var observations = steps.Select(AgentObservation.FromStep).ToArray();
        var response = await chatModel.CompleteAsync(
            new ChatRequest(
                [
                    new ChatMessage(ChatRole.System, "You are Thoth. Answer only from the collected evidence, mention uncertainty, and give the next concrete action."),
                    new ChatMessage(ChatRole.User, request.Goal)
                ],
                request.Model,
                0.2,
                Purpose: ModelRequestPurpose.FinalSynthesis,
                Input: new FinalSynthesisModelInput(request.Goal, stopReason, observations)),
            cancellationToken);

        return string.IsNullOrWhiteSpace(response.Content)
            ? BuildEvidenceFallback(request.Goal, steps, stopReason)
            : response.Content.Trim();
    }

    private static string BuildEvidenceFallback(
        string goal,
        IReadOnlyList<AgentStep> steps,
        string stopReason)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Goal: {goal}");
        builder.AppendLine();
        builder.AppendLine("Observed:");
        foreach (var step in steps)
        {
            builder.AppendLine($"- {step.Invocation?.ToolName ?? "no tool"}: {(step.Result?.Succeeded == true ? "succeeded" : "failed")}");
            if (step.Result is not null)
            {
                builder.AppendLine($"  {Trim(SingleLine(step.Result.Content), 500)}");
            }
        }

        if (!string.IsNullOrWhiteSpace(stopReason))
        {
            builder.AppendLine();
            builder.AppendLine($"Stopped because: {stopReason}");
        }

        return builder.ToString().Trim();
    }

    private static string BuildFingerprint(ToolInvocation invocation) =>
        invocation.ToolName.ToLowerInvariant() + ":" + JsonSerializer.Serialize(
            invocation.Arguments.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase));

    private static string Trim(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum] + "\n[truncated]";

    private static string BuildProjectMemory(string goal, string finalAnswer) =>
        $"Task: {Trim(SingleLine(goal), 220)}\nOutcome: {Trim(SingleLine(finalAnswer), 700)}";

    private static string SingleLine(string value) =>
        string.Join(' ', value.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
}
