using System.Text;
using System.Text.Json;
using Thoth.Core.Chat;
using Thoth.Core.Tools;

namespace Thoth.Core.Agent;

/// <summary>
/// Requests one action at a time. Each call includes all previous observations,
/// so a capable model can revise its plan after every tool result.
/// </summary>
public sealed class ModelAgentDecisionService(
    IChatModel model,
    IAgentDecisionService? fallback = null) : IAgentDecisionService
{
    private readonly IAgentDecisionService fallback = fallback ?? new HeuristicAgentDecisionService();

    public async Task<AgentDecision> DecideAsync(
        AgentDecisionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await model.CompleteAsync(
                new ChatRequest(
                    [
                        new ChatMessage(ChatRole.System, "You are Thoth's action controller. Choose exactly one safe next action from evidence."),
                        new ChatMessage(ChatRole.User, BuildPrompt(context))
                    ],
                    context.Request.Model,
                    0,
                    Purpose: ModelRequestPurpose.AgentDecision,
                    Input: new AgentDecisionModelInput(
                        context.Request,
                        context.Memories,
                        context.Tools.Select(ModelToolDescriptor.FromTool).ToArray(),
                        context.Steps.Select(AgentObservation.FromStep).ToArray())),
                cancellationToken);

            return TryParse(response.Content, context.Tools, context.Steps) ??
                   await fallback.DecideAsync(context, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return await fallback.DecideAsync(context, cancellationToken);
        }
    }

    private static string BuildPrompt(AgentDecisionContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Return one JSON agent decision only; no markdown and no hidden chain-of-thought.");
        builder.AppendLine("Tool action: {\"kind\":\"tool\",\"rationale\":\"short observable reason\",\"tool\":\"name\",\"arguments\":{\"key\":\"value\"}}");
        builder.AppendLine("Final action: {\"kind\":\"final\",\"rationale\":\"short reason\",\"answer\":\"direct final answer\"}");
        builder.AppendLine("Stop action: {\"kind\":\"stop\",\"rationale\":\"why execution cannot safely continue\"}");
        builder.AppendLine();
        builder.AppendLine($"Goal: {context.Request.Goal}");
        builder.AppendLine($"Workspace: {context.Request.WorkingDirectory}");
        builder.AppendLine($"Dry run: {context.Request.DryRun}");
        builder.AppendLine();
        builder.AppendLine("Tools:");

        foreach (var tool in context.Tools)
        {
            var parameters = string.Join(", ", tool.Parameters.Select(parameter =>
                $"{parameter.Name}:{parameter.Type}{(parameter.Required ? " required" : string.Empty)}"));
            builder.AppendLine($"- {tool.Name}: {tool.Description} ({parameters})");
        }

        if (context.Memories.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Relevant memory:");
            foreach (var memory in context.Memories.Take(6))
            {
                builder.AppendLine($"- [{memory.Scope}] {Trim(memory.Content, 700)}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Executed observations:");
        if (context.Steps.Count == 0)
        {
            builder.AppendLine("- none");
        }
        else
        {
            foreach (var step in context.Steps.TakeLast(12))
            {
                builder.AppendLine($"- Step {step.Index}: {step.Thought}");
                if (step.Invocation is not null)
                {
                    builder.AppendLine($"  Tool: {step.Invocation.ToolName} {JsonSerializer.Serialize(step.Invocation.Arguments)}");
                }

                if (step.Result is not null)
                {
                    builder.AppendLine($"  Success: {step.Result.Succeeded}");
                    builder.AppendLine($"  Observation: {Trim(step.Result.Content, 2500)}");
                }
            }
        }

        builder.AppendLine();
        builder.AppendLine("Rules: inspect before editing; do not repeat the same failed call; verify writes; finish only when evidence supports the answer.");
        builder.AppendLine("For public web/current/latest/news/price/weather/research questions, call web.research first when available; otherwise use web.search then web.read. Include source URLs in the final answer.");
        return builder.ToString();
    }

    private static AgentDecision? TryParse(
        string content,
        IReadOnlyList<IAgentTool> tools,
        IReadOnlyList<AgentStep> previousSteps)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(content[start..(end + 1)]);
            var root = document.RootElement;
            var kind = GetString(root, "kind").ToLowerInvariant();
            var rationale = GetString(root, "rationale", "Selected the next observable action.");

            if (kind == "final")
            {
                var answer = GetString(root, "answer");
                return string.IsNullOrWhiteSpace(answer) ? null : AgentDecision.Finish(answer, rationale);
            }

            if (kind == "stop")
            {
                return AgentDecision.Stop(rationale);
            }

            if (kind != "tool")
            {
                return null;
            }

            var toolName = GetString(root, "tool");
            if (!tools.Any(tool => string.Equals(tool.Name, toolName, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            var arguments = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("arguments", out var argumentsElement) &&
                argumentsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in argumentsElement.EnumerateObject())
                {
                    arguments[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Number => property.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Null => null,
                        _ => property.Value.GetRawText()
                    };
                }
            }

            var invocation = new ToolInvocation(toolName, arguments);
            return IsValidInvocation(invocation, tools, previousSteps)
                ? AgentDecision.UseTool(rationale, invocation)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetString(JsonElement element, string property, string fallback = "") =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static bool IsValidInvocation(
        ToolInvocation invocation,
        IReadOnlyList<IAgentTool> tools,
        IReadOnlyList<AgentStep> previousSteps)
    {
        var tool = tools.FirstOrDefault(tool => string.Equals(tool.Name, invocation.ToolName, StringComparison.OrdinalIgnoreCase));
        if (tool is null)
        {
            return false;
        }

        foreach (var parameter in tool.Parameters.Where(parameter => parameter.Required))
        {
            if (!invocation.Arguments.TryGetValue(parameter.Name, out var value) ||
                string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
        }

        var repeatedFailures = previousSteps.Count(step =>
            step.Result?.Succeeded == false &&
            step.Invocation is not null &&
            string.Equals(step.Invocation.ToolName, invocation.ToolName, StringComparison.OrdinalIgnoreCase) &&
            SameArguments(step.Invocation.Arguments, invocation.Arguments));
        return repeatedFailures < 2;
    }

    private static bool SameArguments(
        IReadOnlyDictionary<string, string?> left,
        IReadOnlyDictionary<string, string?> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var item in left)
        {
            if (!right.TryGetValue(item.Key, out var value) ||
                !string.Equals(item.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string Trim(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum] + "\n[truncated]";
}
