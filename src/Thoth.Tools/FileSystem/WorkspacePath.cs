namespace Thoth.Tools.FileSystem;

internal static class WorkspacePath
{
    public static string ResolveInsideWorkspace(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Path is required.", nameof(relativePath));
        }

        var rootFullPath = EnsureTrailingSeparator(Path.GetFullPath(root));
        var targetFullPath = Path.GetFullPath(Path.Combine(rootFullPath, relativePath));

        if (!targetFullPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Path escapes workspace: {relativePath}");
        }

        return targetFullPath;
    }

    public static bool ShouldSkipDirectory(string directoryName)
    {
        return directoryName.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals("node_modules", StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksBinary(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".sqlite", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".db", StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
    }
}
