using System.Text;
using Thoth.Core.Tools;

namespace Thoth.Tools.FileSystem;

public sealed class FileWriteTool : IAgentTool
{
    public string Name => "file.write";

    public string Description => "Writes a text file inside the workspace. Supports overwrite or append mode.";

    public IReadOnlyList<ToolParameter> Parameters { get; } =
    [
        new("path", "Workspace-relative path to write."),
        new("content", "Text content to write."),
        new("mode", "overwrite or append.", false)
    ];

    public async ValueTask<ToolResult> InvokeAsync(
        ToolInvocation invocation,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        var path = invocation.GetString("path");
        var content = invocation.GetString("content");
        var mode = invocation.GetString("mode", "overwrite");
        var fullPath = WorkspacePath.ResolveInsideWorkspace(context.WorkingDirectory, path);

        if (context.DryRun)
        {
            return ToolResult.Success(Name, $"Dry run: would write {content.Length} characters to {path}.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        if (mode.Equals("append", StringComparison.OrdinalIgnoreCase))
        {
            await File.AppendAllTextAsync(fullPath, content, Encoding.UTF8, cancellationToken);
        }
        else
        {
            await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8, cancellationToken);
        }

        return ToolResult.Success(
            Name,
            $"Wrote {content.Length} characters to {path}.",
            new Dictionary<string, string> { ["path"] = path, ["mode"] = mode });
    }
}
