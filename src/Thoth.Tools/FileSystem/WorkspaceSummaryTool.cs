using System.Text;
using System.Text.RegularExpressions;
using Thoth.Core.Tools;

namespace Thoth.Tools.FileSystem;

public sealed class WorkspaceSummaryTool : IAgentTool
{
    public string Name => "workspace.summary";

    public string Description => "Summarizes projects, top-level entries, and HTTP routes in the current workspace.";

    public IReadOnlyList<ToolParameter> Parameters { get; } =
    [
        new("maxEntries", "Maximum top-level entries and endpoints to include.", false, "integer")
    ];

    public ValueTask<ToolResult> InvokeAsync(
        ToolInvocation invocation,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        var maxEntries = Math.Clamp(invocation.GetInt("maxEntries", 40), 5, 120);
        var root = Path.GetFullPath(context.WorkingDirectory);
        var files = EnumerateFiles(root, cancellationToken).ToArray();
        var directories = EnumerateDirectories(root, cancellationToken).ToArray();
        var endpoints = ExtractEndpoints(root, files, cancellationToken).Take(maxEntries).ToArray();
        var builder = new StringBuilder();

        builder.AppendLine($"Workspace: {root}");
        builder.AppendLine($"Files: {files.Length}");
        builder.AppendLine($"Directories: {directories.Length}");
        builder.AppendLine();

        AppendList(
            builder,
            "Projects",
            files.Where(file => file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Select(file => Path.GetRelativePath(root, file))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .Take(maxEntries));

        AppendList(
            builder,
            "Packages",
            files.Where(file => Path.GetFileName(file).Equals("package.json", StringComparison.OrdinalIgnoreCase))
                .Select(file => Path.GetRelativePath(root, file))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .Take(maxEntries));

        AppendList(
            builder,
            "Top level",
            Directory.EnumerateFileSystemEntries(root)
                .Where(path => !WorkspacePath.ShouldSkipDirectory(Path.GetFileName(path)))
                .Select(path => Path.GetRelativePath(root, path) + (Directory.Exists(path) ? Path.DirectorySeparatorChar : string.Empty))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(maxEntries));

        AppendList(builder, "HTTP routes", endpoints);

        return ValueTask.FromResult(ToolResult.Success(
            Name,
            builder.ToString().TrimEnd(),
            new Dictionary<string, string>
            {
                ["files"] = files.Length.ToString(),
                ["directories"] = directories.Length.ToString(),
                ["endpoints"] = endpoints.Length.ToString()
            }));
    }

    private static void AppendList(StringBuilder builder, string title, IEnumerable<string> values)
    {
        builder.AppendLine(title + ":");
        var any = false;

        foreach (var value in values)
        {
            any = true;
            builder.AppendLine($"- {value}");
        }

        if (!any)
        {
            builder.AppendLine("- none");
        }

        builder.AppendLine();
    }

    private static IEnumerable<string> EnumerateFiles(string root, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            string[] childDirectories = [];
            string[] files = [];

            try
            {
                childDirectories = Directory.GetDirectories(directory);
                files = Directory.GetFiles(directory);
            }
            catch (UnauthorizedAccessException)
            {
            }

            foreach (var childDirectory in childDirectories.OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (!WorkspacePath.ShouldSkipDirectory(Path.GetFileName(childDirectory)))
                {
                    pending.Push(childDirectory);
                }
            }

            foreach (var file in files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectories(string root, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            string[] childDirectories = [];

            try
            {
                childDirectories = Directory.GetDirectories(directory);
            }
            catch (UnauthorizedAccessException)
            {
            }

            foreach (var childDirectory in childDirectories.OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (WorkspacePath.ShouldSkipDirectory(Path.GetFileName(childDirectory)))
                {
                    continue;
                }

                yield return childDirectory;
                pending.Push(childDirectory);
            }
        }
    }

    private static IEnumerable<string> ExtractEndpoints(
        string root,
        IEnumerable<string> files,
        CancellationToken cancellationToken)
    {
        foreach (var file in files.Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string text;

            try
            {
                text = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (Match match in Regex.Matches(text, @"app\.Map(?<verb>Get|Post|Put|Patch|Delete)\(\""(?<route>[^\""]+)\"""))
            {
                yield return $"{match.Groups["verb"].Value.ToUpperInvariant()} {match.Groups["route"].Value} ({Path.GetRelativePath(root, file)})";
            }
        }
    }
}
