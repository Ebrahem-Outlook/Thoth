using System.Text;
using Thoth.Core.Tools;

namespace Thoth.Tools.FileSystem;

public sealed class FileListTool : IAgentTool
{
    public string Name => "file.list";

    public string Description => "Lists files and directories under a workspace path.";

    public IReadOnlyList<ToolParameter> Parameters { get; } =
    [
        new("path", "Workspace-relative directory path.", false),
        new("maxResults", "Maximum entries to return.", false, "integer")
    ];

    public ValueTask<ToolResult> InvokeAsync(
        ToolInvocation invocation,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        var path = invocation.GetString("path", ".");
        var maxResults = Math.Clamp(invocation.GetInt("maxResults", 100), 1, 1000);
        var fullPath = WorkspacePath.ResolveInsideWorkspace(context.WorkingDirectory, path);

        if (!Directory.Exists(fullPath))
        {
            return ValueTask.FromResult(ToolResult.Failure(Name, $"Directory not found: {path}"));
        }

        var builder = new StringBuilder();
        var count = 0;

        foreach (var directory in Directory.EnumerateDirectories(fullPath).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.AppendLine(Path.GetRelativePath(context.WorkingDirectory, directory) + Path.DirectorySeparatorChar);
            if (++count >= maxResults)
            {
                break;
            }
        }

        if (count < maxResults)
        {
            foreach (var file in Directory.EnumerateFiles(fullPath).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                builder.AppendLine(Path.GetRelativePath(context.WorkingDirectory, file));
                if (++count >= maxResults)
                {
                    break;
                }
            }
        }

        return ValueTask.FromResult(ToolResult.Success(Name, builder.ToString(), new Dictionary<string, string>
        {
            ["count"] = count.ToString()
        }));
    }
}
