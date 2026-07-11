using Thoth.Core.Memory;
using Thoth.Core.Tools;

namespace Thoth.Core.Agent;

public sealed record AgentDecisionContext(
    AgentRequest Request,
    IReadOnlyList<MemoryRecord> Memories,
    IReadOnlyList<IAgentTool> Tools,
    IReadOnlyList<AgentStep> Steps);
