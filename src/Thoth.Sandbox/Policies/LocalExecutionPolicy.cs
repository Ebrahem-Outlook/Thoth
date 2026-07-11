using Thoth.Core.Configuration;
using Thoth.Core.Sandbox;
using Thoth.Core.Tools;

namespace Thoth.Sandbox.Policies;

public sealed class LocalExecutionPolicy(SandboxOptions options) : IExecutionPolicy
{
    private static readonly HashSet<string> AlwaysAllowedTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "workspace.summary",
        "workspace.map",
        "file.read",
        "file.list",
        "file.info",
        "file.search",
        "http.get",
        "web.search",
        "web.read",
        "web.research",
        "memory.search",
        "memory.recent",
        "memory.write"
    };

    public PolicyDecision Authorize(ToolInvocation invocation, ToolContext context)
    {
        if (AlwaysAllowedTools.Contains(invocation.ToolName))
        {
            return PolicyDecision.Allow();
        }

        if (invocation.ToolName.Equals("file.write", StringComparison.OrdinalIgnoreCase) ||
            invocation.ToolName.Equals("file.patch", StringComparison.OrdinalIgnoreCase))
        {
            if (!options.AllowFileWrites)
            {
                return PolicyDecision.Deny("File writes are disabled.");
            }

            var path = invocation.GetString("path");
            return IsInsideWorkspace(context.WorkingDirectory, path)
                ? PolicyDecision.Allow("File write stays inside workspace.")
                : PolicyDecision.Deny("File write path escapes workspace.");
        }

        if (invocation.ToolName.Equals("shell.run", StringComparison.OrdinalIgnoreCase))
        {
            return AuthorizeShell(invocation);
        }

        return PolicyDecision.Deny($"No policy rule exists for tool '{invocation.ToolName}'.");
    }

    private PolicyDecision AuthorizeShell(ToolInvocation invocation)
    {
        if (!options.AllowShell)
        {
            return PolicyDecision.Deny("Shell execution is disabled.");
        }

        var executable = invocation.GetString("executable");
        var arguments = invocation.GetString("arguments");

        if (string.IsNullOrWhiteSpace(executable))
        {
            return PolicyDecision.Deny("Shell executable is required.");
        }

        if (!options.AllowedShellExecutables.Any(allowed =>
                string.Equals(allowed, executable, StringComparison.OrdinalIgnoreCase)))
        {
            return PolicyDecision.Deny($"Executable '{executable}' is not allowlisted.");
        }

        var commandLine = $" {executable} {arguments} ";
        foreach (var blocked in options.BlockedCommandTokens)
        {
            if (commandLine.Contains(blocked, StringComparison.OrdinalIgnoreCase))
            {
                return PolicyDecision.Deny($"Blocked command token: {blocked.Trim()}");
            }
        }

        return PolicyDecision.Allow("Shell command passed local policy.");
    }

    private static bool IsInsideWorkspace(string workspaceRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var root = EnsureTrailingSeparator(Path.GetFullPath(workspaceRoot));
        var target = Path.GetFullPath(Path.Combine(root, relativePath));
        return target.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
    }
}
