using Thoth.Core.Tools;

namespace Thoth.Tools.Memory;

public sealed class MemoryWriteTool : IAgentTool
{
    public string Name => "memory.write";

    public string Description => "Adds a note to Thoth's local memory store.";

    public IReadOnlyList<ToolParameter> Parameters { get; } =
    [
        new("scope", "Memory scope such as project, run, or user.", false),
        new("content", "Memory content to store.")
    ];

    public async ValueTask<ToolResult> InvokeAsync(
        ToolInvocation invocation,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        var scope = invocation.GetString("scope", "project");
        var content = invocation.GetString("content");

        if (string.IsNullOrWhiteSpace(content))
        {
            return ToolResult.Failure(Name, "Memory content is required.");
        }

        var record = await context.Memory.AddAsync(scope, content, cancellationToken: cancellationToken);
        return ToolResult.Success(Name, $"Stored memory {record.Id:N} in scope '{scope}'.");
    }
}
