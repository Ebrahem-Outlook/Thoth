using Thoth.Core.Configuration;
using Thoth.Core.Memory;
using Thoth.Core.Tools;
using Thoth.Sandbox.Policies;

namespace Thoth.Tests.Tools;

public sealed class PolicyTests
{
    [Fact]
    public void Authorize_DeniesFileWriteOutsideWorkspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "thoth-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        var policy = new LocalExecutionPolicy(new SandboxOptions { AllowFileWrites = true });
        var context = new ToolContext(workspace, new InMemoryMemoryStore(), policy);
        var invocation = new ToolInvocation(
            "file.write",
            new Dictionary<string, string?> { ["path"] = "..\\outside.txt", ["content"] = "nope" });

        var decision = policy.Authorize(invocation, context);

        Assert.False(decision.Allowed);
    }

    [Fact]
    public void Authorize_DeniesShellByDefault()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "thoth-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        var policy = new LocalExecutionPolicy(new SandboxOptions());
        var context = new ToolContext(workspace, new InMemoryMemoryStore(), policy);
        var invocation = new ToolInvocation(
            "shell.run",
            new Dictionary<string, string?> { ["executable"] = "dotnet", ["arguments"] = "--info" });

        var decision = policy.Authorize(invocation, context);

        Assert.False(decision.Allowed);
    }
}
