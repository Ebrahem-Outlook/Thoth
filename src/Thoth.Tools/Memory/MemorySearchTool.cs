using System.Text;
using Thoth.Core.Tools;

namespace Thoth.Tools.Memory;

public sealed class MemorySearchTool : IAgentTool
{
    public string Name => "memory.search";

    public string Description => "Searches Thoth's local memory store.";

    public IReadOnlyList<ToolParameter> Parameters { get; } =
    [
        new("query", "Memory search query."),
        new("scope", "Optional memory scope.", false),
        new("limit", "Maximum records to return.", false, "integer")
    ];

    public async ValueTask<ToolResult> InvokeAsync(
        ToolInvocation invocation,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        var query = invocation.GetString("query");
        var scope = invocation.GetString("scope", string.Empty);
        var limit = Math.Clamp(invocation.GetInt("limit", 8), 1, 50);
        var records = await context.Memory.SearchAsync(
            query,
            string.IsNullOrWhiteSpace(scope) ? null : scope,
            limit,
            cancellationToken);

        if (records.Count == 0)
        {
            return ToolResult.Success(Name, "No memory records found.");
        }

        var builder = new StringBuilder();
        foreach (var record in records)
        {
            builder.AppendLine($"[{record.CreatedAt:u}] {record.Scope}: {record.Content}");
        }

        return ToolResult.Success(Name, builder.ToString(), new Dictionary<string, string> { ["count"] = records.Count.ToString() });
    }
}
