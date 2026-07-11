using Thoth.Core.Tools;
using Thoth.Tools.FileSystem;
using Thoth.Tools.Http;
using Thoth.Tools.Memory;
using Thoth.Tools.Shell;
using Thoth.Tools.Web;

namespace Thoth.Tools;

public static class DefaultToolSet
{
    public static ToolRegistry Create(TimeSpan shellTimeout)
    {
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        return new ToolRegistry()
            .Register(new WorkspaceSummaryTool())
            .Register(new WorkspaceMapTool())
            .Register(new FileListTool())
            .Register(new FileInfoTool())
            .Register(new FileReadTool())
            .Register(new FileSearchTool())
            .Register(new FileWriteTool())
            .Register(new FilePatchTool())
            .Register(new MemorySearchTool())
            .Register(new MemoryRecentTool())
            .Register(new MemoryWriteTool())
            .Register(new WebSearchTool(httpClient))
            .Register(new WebReadTool(httpClient))
            .Register(new WebResearchTool(httpClient))
            .Register(new HttpGetTool(httpClient))
            .Register(new ShellRunTool(shellTimeout));
    }
}
