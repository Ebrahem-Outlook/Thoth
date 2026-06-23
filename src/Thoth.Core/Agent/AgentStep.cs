using Thoth.Core.Tools;

namespace Thoth.Core.Agent;

public sealed record AgentStep(
    int Index,
    string Thought,
    ToolInvocation? Invocation,
    ToolResult? Result,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);
