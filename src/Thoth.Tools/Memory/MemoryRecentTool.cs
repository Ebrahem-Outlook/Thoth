using System.Text;
using Thoth.Core.Tools;

namespace Thoth.Tools.Memory;

public sealed class MemoryRecentTool : IAgentTool
{
    public string Name => "memory.recent";

    public string Description => "Returns recent local memory records.";

    public IReadOnlyList<ToolParameter> Parameters { get; } =
    [
        new("scope", "Optional memory scope.", false),
        new("limit", "Maximum records to return.", false, "integer")
    ];

    public async ValueTask<ToolResult> InvokeAsync(
        ToolInvocation invocation,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        var records = await context.Memory.RecentAsync(
            invocation.GetString("scope", string.Empty),
            Math.Clamp(invocation.GetInt("limit", 8), 1, 50),
            cancellationToken);

        if (records.Count == 0)
        {
            return ToolResult.Success(Name, "No recent memory records.");
        }

        var builder = new StringBuilder();
        foreach (var record in records)
        {
            builder.AppendLine($"[{record.CreatedAt:u}] {record.Scope}: {record.Content}");
        }

        return ToolResult.Success(Name, builder.ToString(), new Dictionary<string, string> { ["count"] = records.Count.ToString() });
    }
}
