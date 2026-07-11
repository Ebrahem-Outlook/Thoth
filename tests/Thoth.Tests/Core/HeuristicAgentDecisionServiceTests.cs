using Thoth.Core.Agent;
using Thoth.Core.Tools;

namespace Thoth.Tests.Core;

public sealed class HeuristicAgentDecisionServiceTests
{
    [Fact]
    public async Task DecideAsync_UsesWebResearchFirstForExternalSearch()
    {
        var service = new HeuristicAgentDecisionService();
        var context = new AgentDecisionContext(
            new AgentRequest(
                "search the web for LangGraph and summarize it",
                Directory.GetCurrentDirectory(),
                "thoth-self"),
            [],
            [new StubTool("web.research"), new StubTool("workspace.summary")],
            []);

        var decision = await service.DecideAsync(context);

        Assert.Equal(AgentDecisionKind.Tool, decision.Kind);
        Assert.Equal("web.research", decision.Invocation?.ToolName);
        Assert.Equal("LangGraph", decision.Invocation?.GetString("query"));
    }

    [Fact]
    public async Task DecideAsync_KeepsRepositorySearchOnWorkspaceTools()
    {
        var service = new HeuristicAgentDecisionService();
        var context = new AgentDecisionContext(
            new AgentRequest(
                "search the repo for Program.cs",
                Directory.GetCurrentDirectory(),
                "thoth-self"),
            [],
            [new StubTool("web.research"), new StubTool("workspace.summary"), new StubTool("file.search")],
            []);

        var decision = await service.DecideAsync(context);

        Assert.Equal(AgentDecisionKind.Tool, decision.Kind);
        Assert.Equal("workspace.summary", decision.Invocation?.ToolName);
    }

    private sealed class StubTool(string name) : IAgentTool
    {
        public string Name { get; } = name;

        public string Description => "Test tool.";

        public IReadOnlyList<ToolParameter> Parameters => [];

        public ValueTask<ToolResult> InvokeAsync(
            ToolInvocation invocation,
            ToolContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolResult.Success(Name, string.Empty));
    }
}
