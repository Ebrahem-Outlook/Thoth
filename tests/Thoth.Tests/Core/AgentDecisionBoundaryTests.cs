using Thoth.Core.Agent;
using Thoth.Core.Chat;
using Thoth.Core.Memory;
using Thoth.Core.Tools;

namespace Thoth.Tests.Core;

public sealed class AgentDecisionBoundaryTests
{
    [Fact]
    public async Task DecideAsync_RejectsUnknownToolAndUsesFallback()
    {
        var fallback = new StubDecisionService(AgentDecision.Stop("fallback"));
        var service = new ModelAgentDecisionService(
            new ScriptedModel("""{"kind":"tool","rationale":"x","tool":"unknown.tool","arguments":{}}"""),
            fallback);

        var decision = await service.DecideAsync(Context());

        Assert.Equal(AgentDecisionKind.Stop, decision.Kind);
        Assert.True(fallback.Called);
    }

    [Fact]
    public async Task DecideAsync_RejectsMissingRequiredArgumentsAndUsesFallback()
    {
        var fallback = new StubDecisionService(AgentDecision.Stop("fallback"));
        var service = new ModelAgentDecisionService(
            new ScriptedModel("""{"kind":"tool","rationale":"x","tool":"file.read","arguments":{}}"""),
            fallback);

        var decision = await service.DecideAsync(Context());

        Assert.Equal(AgentDecisionKind.Stop, decision.Kind);
        Assert.True(fallback.Called);
    }

    [Fact]
    public async Task DecideAsync_RejectsRepeatedFailedToolCallAndUsesFallback()
    {
        var invocation = new ToolInvocation("file.read", new Dictionary<string, string?> { ["path"] = "missing.cs" });
        var failedSteps = new[]
        {
            new AgentStep(1, "read", invocation, ToolResult.Failure("file.read", "missing"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new AgentStep(2, "read", invocation, ToolResult.Failure("file.read", "missing"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        };
        var fallback = new StubDecisionService(AgentDecision.Stop("fallback"));
        var service = new ModelAgentDecisionService(
            new ScriptedModel("""{"kind":"tool","rationale":"x","tool":"file.read","arguments":{"path":"missing.cs"}}"""),
            fallback);

        var decision = await service.DecideAsync(Context(failedSteps));

        Assert.Equal(AgentDecisionKind.Stop, decision.Kind);
        Assert.True(fallback.Called);
    }

    private static AgentDecisionContext Context(IReadOnlyList<AgentStep>? steps = null) =>
        new(
            new AgentRequest("inspect file", Directory.GetCurrentDirectory(), "thoth-self"),
            Array.Empty<MemoryRecord>(),
            [new StubTool("file.read", [new ToolParameter("path", "Path to read.")])],
            steps ?? []);

    private sealed class ScriptedModel(string content) : IChatModel
    {
        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(content, request.Model));
    }

    private sealed class StubDecisionService(AgentDecision decision) : IAgentDecisionService
    {
        public bool Called { get; private set; }

        public Task<AgentDecision> DecideAsync(AgentDecisionContext context, CancellationToken cancellationToken = default)
        {
            Called = true;
            return Task.FromResult(decision);
        }
    }

    private sealed class StubTool(string name, IReadOnlyList<ToolParameter> parameters) : IAgentTool
    {
        public string Name { get; } = name;

        public string Description => "Test tool.";

        public IReadOnlyList<ToolParameter> Parameters { get; } = parameters;

        public ValueTask<ToolResult> InvokeAsync(ToolInvocation invocation, ToolContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolResult.Success(Name, string.Empty));
    }
}
