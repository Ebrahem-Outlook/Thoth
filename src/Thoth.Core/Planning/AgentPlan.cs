using Thoth.Core.Tools;

namespace Thoth.Core.Planning;

public sealed record AgentPlan(
    string Summary,
    IReadOnlyList<AgentPlanStep> Steps,
    string Source);

public sealed record AgentPlanStep(
    string Thought,
    ToolInvocation? Invocation);
