using Thoth.Core.Tools;
using Thoth.Tools.FileSystem;
using Thoth.Tools.Memory;
using Thoth.Tools.Shell;

namespace Thoth.Tools;

public static class DefaultToolSet
{
    public static ToolRegistry Create(TimeSpan shellTimeout)
    {
        return new ToolRegistry()
            .Register(new WorkspaceMapTool())
            .Register(new FileReadTool())
            .Register(new FileSearchTool())
            .Register(new FileWriteTool())
            .Register(new MemorySearchTool())
            .Register(new MemoryWriteTool())
            .Register(new ShellRunTool(shellTimeout));
    }
}
