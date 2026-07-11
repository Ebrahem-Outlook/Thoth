using Thoth.Core.Configuration;
using Thoth.Core.Memory;
using Thoth.Core.Tools;
using Thoth.Sandbox.Policies;
using Thoth.Tools.FileSystem;

namespace Thoth.Tests.Tools;

public sealed class FilePatchToolTests
{
    [Fact]
    public async Task InvokeAsync_RefusesAmbiguousPatch()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "thoth-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(Path.Combine(workspace, "sample.txt"), "old old");
        var policy = new LocalExecutionPolicy(new SandboxOptions { AllowFileWrites = true });
        var context = new ToolContext(workspace, new InMemoryMemoryStore(), policy);
        var invocation = new ToolInvocation("file.patch", new Dictionary<string, string?>
        {
            ["path"] = "sample.txt",
            ["oldText"] = "old",
            ["newText"] = "new",
            ["expectedOccurrences"] = "1"
        });

        var result = await new FilePatchTool().InvokeAsync(invocation, context);

        Assert.False(result.Succeeded);
        Assert.Equal("old old", await File.ReadAllTextAsync(Path.Combine(workspace, "sample.txt")));
    }
}
