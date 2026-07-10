using Thoth.Core.Configuration;

namespace Thoth.Api.Services;

public sealed class WorkspaceInspectionService
{
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".angular",
        "bin",
        "obj",
        "dist",
        "node_modules"
    };

    public WorkspaceSummary Inspect(string workspaceRoot)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var files = EnumerateFiles(root).ToArray();
        var directories = EnumerateDirectories(root).ToArray();
        var endpoints = ExtractEndpoints(files);

        return new WorkspaceSummary(
            root,
            DateTimeOffset.UtcNow,
            files.Length,
            directories.Length,
            files.Where(file => file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Select(file => Path.GetRelativePath(root, file))
                .OrderBy(file => file)
                .ToArray(),
            files.Where(file => Path.GetFileName(file).Equals("package.json", StringComparison.OrdinalIgnoreCase))
                .Select(file => Path.GetRelativePath(root, file))
                .OrderBy(file => file)
                .ToArray(),
            Directory.EnumerateFileSystemEntries(root)
                .Where(path => !SkippedDirectories.Contains(Path.GetFileName(path)))
                .Select(path => Path.GetRelativePath(root, path) + (Directory.Exists(path) ? Path.DirectorySeparatorChar : string.Empty))
                .OrderBy(path => path)
                .Take(32)
                .ToArray(),
            endpoints);
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
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

            foreach (var childDirectory in childDirectories)
            {
                if (!SkippedDirectories.Contains(Path.GetFileName(childDirectory)))
                {
                    pending.Push(childDirectory);
                }
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectories(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] childDirectories = [];

            try
            {
                childDirectories = Directory.GetDirectories(directory);
            }
            catch (UnauthorizedAccessException)
            {
            }

            foreach (var childDirectory in childDirectories)
            {
                if (SkippedDirectories.Contains(Path.GetFileName(childDirectory)))
                {
                    continue;
                }

                yield return childDirectory;
                pending.Push(childDirectory);
            }
        }
    }

    private static IReadOnlyList<WorkspaceEndpoint> ExtractEndpoints(IReadOnlyList<string> files)
    {
        var endpoints = new List<WorkspaceEndpoint>();
        foreach (var file in files.Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var text = File.ReadAllText(file);
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    line,
                    @"app\.Map(?<verb>Get|Post|Put|Patch|Delete)\(\""(?<route>[^\""]+)\""");

                if (match.Success)
                {
                    endpoints.Add(new WorkspaceEndpoint(
                        match.Groups["verb"].Value.ToUpperInvariant(),
                        match.Groups["route"].Value,
                        file));
                }
            }
        }

        return endpoints
            .DistinctBy(endpoint => $"{endpoint.Method}:{endpoint.Route}")
            .OrderBy(endpoint => endpoint.Route)
            .ToArray();
    }
}

public sealed record WorkspaceSummary(
    string Root,
    DateTimeOffset GeneratedAt,
    int FileCount,
    int DirectoryCount,
    IReadOnlyList<string> DotNetProjects,
    IReadOnlyList<string> PackageFiles,
    IReadOnlyList<string> TopLevelEntries,
    IReadOnlyList<WorkspaceEndpoint> Endpoints);

public sealed record WorkspaceEndpoint(string Method, string Route, string SourceFile);

public sealed record SystemStatus(
    string RuntimeMode,
    string Model,
    bool SelfContainedOnly,
    bool ShellEnabled,
    int ToolCount,
    int ConversationCount,
    int MemoryCount,
    DateTimeOffset Time);
