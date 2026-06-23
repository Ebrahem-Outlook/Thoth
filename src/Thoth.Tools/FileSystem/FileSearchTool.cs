using System.Text;
using Thoth.Core.Tools;

namespace Thoth.Tools.FileSystem;

public sealed class FileSearchTool : IAgentTool
{
    public string Name => "file.search";

    public string Description => "Searches workspace file names and text contents for a query.";

    public IReadOnlyList<ToolParameter> Parameters { get; } =
    [
        new("query", "Text to search for."),
        new("maxResults", "Maximum matches to return.", false, "integer")
    ];

    public async ValueTask<ToolResult> InvokeAsync(
        ToolInvocation invocation,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        var query = invocation.GetString("query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolResult.Failure(Name, "Search query is required.");
        }

        var maxResults = Math.Clamp(invocation.GetInt("maxResults", 25), 1, 250);
        var root = Path.GetFullPath(context.WorkingDirectory);
        var builder = new StringBuilder();
        var matches = 0;

        foreach (var file in EnumerateFiles(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(root, file);

            if (relative.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                AppendMatch($"{relative}: file name match");
            }

            if (matches >= maxResults)
            {
                break;
            }

            if (WorkspacePath.LooksBinary(file) || new FileInfo(file).Length > 768 * 1024)
            {
                continue;
            }

            try
            {
                var lineNumber = 0;
                await foreach (var line in ReadLinesAsync(file, cancellationToken))
                {
                    lineNumber++;
                    if (line.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        AppendMatch($"{relative}:{lineNumber}: {line.Trim()}");
                    }

                    if (matches >= maxResults)
                    {
                        break;
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            if (matches >= maxResults)
            {
                break;
            }
        }

        return ToolResult.Success(
            Name,
            matches == 0 ? "No matches found." : builder.ToString(),
            new Dictionary<string, string> { ["matches"] = matches.ToString() });

        void AppendMatch(string line)
        {
            if (matches >= maxResults)
            {
                return;
            }

            matches++;
            builder.AppendLine(line);
        }
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] directories = [];
            string[] files = [];

            try
            {
                directories = Directory.GetDirectories(directory);
                files = Directory.GetFiles(directory);
            }
            catch (UnauthorizedAccessException)
            {
            }

            foreach (var childDirectory in directories.OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
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

    private static async IAsyncEnumerable<string> ReadLinesAsync(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is not null)
            {
                yield return line;
            }
        }
    }
}
