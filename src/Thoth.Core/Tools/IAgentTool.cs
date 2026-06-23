namespace Thoth.Core.Tools;

public interface IAgentTool
{
    string Name { get; }

    string Description { get; }

    IReadOnlyList<ToolParameter> Parameters { get; }

    ValueTask<ToolResult> InvokeAsync(
        ToolInvocation invocation,
        ToolContext context,
        CancellationToken cancellationToken = default);
}
