namespace Thoth.Core.Planning;

public interface IAgentPlanner
{
    Task<AgentPlan> CreatePlanAsync(
        AgentPlanningContext context,
        CancellationToken cancellationToken = default);
}
