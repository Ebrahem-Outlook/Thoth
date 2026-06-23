using System.Text;
using System.Text.Json;
using Thoth.Core.Chat;
using Thoth.Core.Tools;

namespace Thoth.Core.Planning;

public sealed class JsonAgentPlanner(IChatModel model, IAgentPlanner? fallback = null) : IAgentPlanner
{
    private readonly IAgentPlanner fallback = fallback ?? new HeuristicAgentPlanner();

    public async Task<AgentPlan> CreatePlanAsync(
        AgentPlanningContext context,
        CancellationToken cancellationToken = default)
    {
        var prompt = BuildPrompt(context);
        var response = await model.CompleteAsync(
            new ChatRequest(
                [
                    new ChatMessage(ChatRole.System, "You plan safe, inspectable tool use for an AI coding agent."),
                    new ChatMessage(ChatRole.User, prompt)
                ],
                context.Request.Model,
                0),
            cancellationToken);

        var parsed = TryParse(response.Content, context.Tools);
        if (parsed is not null && parsed.Steps.Count > 0)
        {
            return parsed;
        }

        return await fallback.CreatePlanAsync(context, cancellationToken);
    }

    private static string BuildPrompt(AgentPlanningContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Return a JSON plan only. Do not include markdown.");
        builder.AppendLine("JSON shape: {\"summary\":\"...\",\"steps\":[{\"thought\":\"...\",\"tool\":\"tool.name\",\"arguments\":{\"key\":\"value\"}}]}");
        builder.AppendLine();
        builder.AppendLine($"Goal: {context.Request.Goal}");
        builder.AppendLine($"Working directory: {context.Request.WorkingDirectory}");
        builder.AppendLine();
        builder.AppendLine("Available tools:");

        foreach (var tool in context.Tools)
        {
            var parameters = string.Join(", ", tool.Parameters.Select(parameter => $"{parameter.Name}:{parameter.Type}"));
            builder.AppendLine($"- {tool.Name}: {tool.Description} Parameters: {parameters}");
        }

        if (context.Memories.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Relevant memories:");
            foreach (var memory in context.Memories)
            {
                builder.AppendLine($"- [{memory.Scope}] {memory.Content}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Prefer read/search/map tools before write or shell tools.");
        return builder.ToString();
    }

    private static AgentPlan? TryParse(string content, IReadOnlyList<IAgentTool> availableTools)
    {
        var json = ExtractJsonObject(content);
        if (json is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var summary = root.TryGetProperty("summary", out var summaryElement)
                ? summaryElement.GetString() ?? "Model-generated plan."
                : "Model-generated plan.";

            if (!root.TryGetProperty("steps", out var stepsElement) || stepsElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var knownTools = availableTools.Select(tool => tool.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var steps = new List<AgentPlanStep>();

            foreach (var stepElement in stepsElement.EnumerateArray())
            {
                var thought = stepElement.TryGetProperty("thought", out var thoughtElement)
                    ? thoughtElement.GetString() ?? "Use a tool."
                    : "Use a tool.";

                if (!stepElement.TryGetProperty("tool", out var toolElement))
                {
                    steps.Add(new AgentPlanStep(thought, null));
                    continue;
                }

                var toolName = toolElement.GetString();
                if (string.IsNullOrWhiteSpace(toolName) || !knownTools.Contains(toolName))
                {
                    continue;
                }

                var arguments = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                if (stepElement.TryGetProperty("arguments", out var argumentsElement) && argumentsElement.ValueKind == JsonValueKind.Object)
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

                steps.Add(new AgentPlanStep(thought, new ToolInvocation(toolName, arguments)));
            }

            return new AgentPlan(summary, steps, "model-json");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractJsonObject(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');

        if (start < 0 || end <= start)
        {
            return null;
        }

        return content[start..(end + 1)];
    }
}
