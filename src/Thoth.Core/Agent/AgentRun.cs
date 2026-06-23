using Thoth.Core.Planning;

namespace Thoth.Core.Agent;

public sealed record AgentRun(
    Guid RunId,
    string Goal,
    string WorkingDirectory,
    AgentPlan Plan,
    IReadOnlyList<AgentStep> Steps,
    string FinalAnswer,
    bool Succeeded,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);
