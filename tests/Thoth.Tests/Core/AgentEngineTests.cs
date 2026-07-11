using Thoth.Core.Agent;
using Thoth.Core.Configuration;
using Thoth.Core.Memory;
using Thoth.Core.Planning;
using Thoth.Llm.Models;
using Thoth.Sandbox.Policies;
using Thoth.Tools;

namespace Thoth.Tests.Core;

public sealed class AgentEngineTests
{
    [Fact]
    public async Task RunAsync_UsesSelfModelAndWorkspaceTools()
    {
        var workspace = CreateWorkspace();
        await File.WriteAllTextAsync(Path.Combine(workspace, "Program.cs"), "Console.WriteLine(\"Hello Thoth\");");

        var memory = new InMemoryMemoryStore();
        var model = new SelfContainedReasoningModel();
        var tools = DefaultToolSet.Create(TimeSpan.FromSeconds(2));
        var policy = new LocalExecutionPolicy(new SandboxOptions());
        var planner = new JsonAgentPlanner(model, new HeuristicAgentPlanner());
        var engine = new AgentEngine(model, planner, tools, memory, policy);

        var run = await engine.RunAsync(new AgentRequest(
            "summarize Program.cs in this workspace",
            workspace,
            "thoth-self",
            MaxSteps: 6));

        Assert.True(run.Succeeded);
        Assert.Contains(run.Steps, step => step.Invocation?.ToolName == "workspace.summary");
        Assert.Contains(run.Steps, step => step.Invocation?.ToolName == "workspace.map");
        Assert.Contains(run.Steps, step => step.Invocation?.ToolName == "file.read");
        Assert.Contains("local semantic brain", run.FinalAnswer);

        var memories = await memory.RecentAsync(limit: 20);
        Assert.DoesNotContain(memories, record => record.Scope == "run");
        Assert.Contains(memories, record => record.Scope == "project" && record.Metadata.TryGetValue("kind", out var kind) && kind == "agent_outcome");
    }

    private static string CreateWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "thoth-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
