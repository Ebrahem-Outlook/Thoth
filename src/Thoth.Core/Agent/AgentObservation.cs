using System.Text.Json;

namespace Thoth.Core.Agent;

public sealed record AgentObservation(
    int Step,
    string Tool,
    bool Succeeded,
    string Summary,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public static AgentObservation FromStep(AgentStep step)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (step.Invocation is not null)
        {
            foreach (var argument in step.Invocation.Arguments)
            {
                if (argument.Value is not null)
                {
                    metadata[argument.Key] = argument.Value;
                }
            }

            metadata["argumentsJson"] = JsonSerializer.Serialize(step.Invocation.Arguments);
        }

        if (step.Result?.Metadata is not null)
        {
            foreach (var item in step.Result.Metadata)
            {
                metadata[$"result.{item.Key}"] = item.Value;
            }
        }

        return new AgentObservation(
            step.Index,
            step.Invocation?.ToolName ?? "none",
            step.Result?.Succeeded == true,
            Trim(step.Result?.Content ?? step.Thought, 2000),
            metadata);
    }

    private static string Trim(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum] + "\n[truncated]";
}
