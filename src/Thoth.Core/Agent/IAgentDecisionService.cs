namespace Thoth.Core.Agent;

public interface IAgentDecisionService
{
    Task<AgentDecision> DecideAsync(
        AgentDecisionContext context,
        CancellationToken cancellationToken = default);
}
