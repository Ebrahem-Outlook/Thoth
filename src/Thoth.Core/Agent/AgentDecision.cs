using Thoth.Core.Tools;

namespace Thoth.Core.Agent;

public enum AgentDecisionKind
{
    Tool,
    Final,
    Stop
}

public sealed record AgentDecision(
    AgentDecisionKind Kind,
    string Rationale,
    ToolInvocation? Invocation = null,
    string? Answer = null)
{
    public static AgentDecision UseTool(string rationale, ToolInvocation invocation) =>
        new(AgentDecisionKind.Tool, rationale, invocation);

    public static AgentDecision Finish(string answer, string rationale = "The task has enough evidence for a final answer.") =>
        new(AgentDecisionKind.Final, rationale, Answer: answer);

    public static AgentDecision Stop(string rationale) =>
        new(AgentDecisionKind.Stop, rationale);
}
