using Thoth.Core.Tools;

namespace Thoth.Tools.FileSystem;

public sealed class FileInfoTool : IAgentTool
{
    public string Name => "file.info";

    public string Description => "Returns metadata for a workspace file or directory.";

    public IReadOnlyList<ToolParameter> Parameters { get; } =
    [
        new("path", "Workspace-relative file or directory path.")
    ];

    public ValueTask<ToolResult> InvokeAsync(
        ToolInvocation invocation,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        var path = invocation.GetString("path");
        var fullPath = WorkspacePath.ResolveInsideWorkspace(context.WorkingDirectory, path);

        if (File.Exists(fullPath))
        {
            var info = new FileInfo(fullPath);
            return ValueTask.FromResult(ToolResult.Success(Name, $$"""
            Path: {{path}}
            Type: file
            Size: {{info.Length}} bytes
            Created: {{info.CreationTimeUtc:u}}
            Modified: {{info.LastWriteTimeUtc:u}}
            Extension: {{info.Extension}}
            """));
        }

        if (Directory.Exists(fullPath))
        {
            var info = new DirectoryInfo(fullPath);
            return ValueTask.FromResult(ToolResult.Success(Name, $$"""
            Path: {{path}}
            Type: directory
            Created: {{info.CreationTimeUtc:u}}
            Modified: {{info.LastWriteTimeUtc:u}}
            """));
        }

        return ValueTask.FromResult(ToolResult.Failure(Name, $"Path not found: {path}"));
    }
}
