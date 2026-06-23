using Thoth.Core.Tools;

namespace Thoth.Tools.FileSystem;

public sealed class FileReadTool : IAgentTool
{
    public string Name => "file.read";

    public string Description => "Reads a text file from inside the workspace.";

    public IReadOnlyList<ToolParameter> Parameters { get; } =
    [
        new("path", "Workspace-relative path to read."),
        new("maxChars", "Maximum number of characters to return.", false, "integer")
    ];

    public async ValueTask<ToolResult> InvokeAsync(
        ToolInvocation invocation,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        var path = invocation.GetString("path");
        var maxChars = Math.Clamp(invocation.GetInt("maxChars", 20000), 1, 200000);
        var fullPath = WorkspacePath.ResolveInsideWorkspace(context.WorkingDirectory, path);

        if (!File.Exists(fullPath))
        {
            return ToolResult.Failure(Name, $"File not found: {path}");
        }

        if (WorkspacePath.LooksBinary(fullPath))
        {
            return ToolResult.Failure(Name, $"Refusing to read binary file: {path}");
        }

        var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
        var truncated = content.Length > maxChars;
        if (truncated)
        {
            content = content[..maxChars] + "\n[truncated]";
        }

        return ToolResult.Success(
            Name,
            content,
            new Dictionary<string, string>
            {
                ["path"] = path,
                ["truncated"] = truncated.ToString()
            });
    }
}
