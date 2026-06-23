namespace Thoth.Runtime;

public static class ThothPathDiscovery
{
    public static string FindWorkspaceRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Thoth.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Path.GetFullPath(startDirectory);
    }
}
