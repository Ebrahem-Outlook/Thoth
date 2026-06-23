using System.Text;
using Thoth.Core.Tools;

namespace Thoth.Tools.FileSystem;

public sealed class WorkspaceMapTool : IAgentTool
{
    public string Name => "workspace.map";

    public string Description => "Builds a compact file tree for the current workspace.";

    public IReadOnlyList<ToolParameter> Parameters { get; } =
    [
        new("maxDepth", "Maximum directory depth to include.", false, "integer")
    ];

    public ValueTask<ToolResult> InvokeAsync(
        ToolInvocation invocation,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        var maxDepth = Math.Clamp(invocation.GetInt("maxDepth", 4), 1, 12);
        var root = Path.GetFullPath(context.WorkingDirectory);
        var builder = new StringBuilder();
        var count = 0;

        builder.AppendLine(Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)) + "/");
        Walk(root, 0);

        return ValueTask.FromResult(ToolResult.Success(
            Name,
            builder.ToString(),
            new Dictionary<string, string> { ["entries"] = count.ToString() }));

        void Walk(string directory, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (depth >= maxDepth || count > 500)
            {
                return;
            }

            IEnumerable<string> directories;
            IEnumerable<string> files;

            try
            {
                directories = Directory.EnumerateDirectories(directory)
                    .Where(path => !WorkspacePath.ShouldSkipDirectory(Path.GetFileName(path)))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                files = Directory.EnumerateFiles(directory)
                    .Where(path => !WorkspacePath.LooksBinary(path))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            foreach (var childDirectory in directories)
            {
                AppendEntry(depth + 1, Path.GetFileName(childDirectory) + "/");
                Walk(childDirectory, depth + 1);
            }

            foreach (var file in files)
            {
                AppendEntry(depth + 1, Path.GetFileName(file));
            }
        }

        void AppendEntry(int depth, string name)
        {
            count++;
            builder.Append(' ', depth * 2);
            builder.AppendLine(name);
        }
    }
}
