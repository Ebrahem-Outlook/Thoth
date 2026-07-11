using System.Text;
using Thoth.Core.Tools;

namespace Thoth.Tools.FileSystem;

public sealed class FilePatchTool : IAgentTool
{
    public string Name => "file.patch";

    public string Description => "Atomically replaces an exact text fragment inside a workspace file and fails on ambiguous matches.";

    public IReadOnlyList<ToolParameter> Parameters { get; } =
    [
        new("path", "Workspace-relative file path."),
        new("oldText", "Exact existing text to replace."),
        new("newText", "Replacement text."),
        new("expectedOccurrences", "Required number of exact matches; defaults to 1.", false, "integer")
    ];

    public async ValueTask<ToolResult> InvokeAsync(
        ToolInvocation invocation,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        var path = invocation.GetString("path");
        var oldText = invocation.GetString("oldText");
        var newText = invocation.GetString("newText");
        var expectedOccurrences = Math.Clamp(invocation.GetInt("expectedOccurrences", 1), 1, 1000);
        var fullPath = WorkspacePath.ResolveInsideWorkspace(context.WorkingDirectory, path);

        if (!File.Exists(fullPath))
        {
            return ToolResult.Failure(Name, $"File not found: {path}");
        }

        if (WorkspacePath.LooksBinary(fullPath))
        {
            return ToolResult.Failure(Name, $"Refusing to patch binary file: {path}");
        }

        if (string.IsNullOrEmpty(oldText))
        {
            return ToolResult.Failure(Name, "oldText cannot be empty.");
        }

        var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
        var occurrences = CountOccurrences(content, oldText);
        if (occurrences != expectedOccurrences)
        {
            return ToolResult.Failure(
                Name,
                $"Patch expected {expectedOccurrences} occurrence(s) but found {occurrences}. No file was changed.",
                new Dictionary<string, string>
                {
                    ["path"] = path,
                    ["occurrences"] = occurrences.ToString()
                });
        }

        var updated = content.Replace(oldText, newText, StringComparison.Ordinal);
        if (context.DryRun)
        {
            return ToolResult.Success(
                Name,
                $"Dry run: patch would replace {occurrences} occurrence(s) in {path}.",
                new Dictionary<string, string> { ["path"] = path, ["changed"] = "false" });
        }

        var temporaryPath = fullPath + ".thoth-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return ToolResult.Success(
            Name,
            $"Patched {path}; replaced {occurrences} exact occurrence(s).",
            new Dictionary<string, string>
            {
                ["path"] = path,
                ["occurrences"] = occurrences.ToString(),
                ["changed"] = "true"
            });
    }

    private static int CountOccurrences(string content, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
