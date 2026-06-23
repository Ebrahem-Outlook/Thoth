using Thoth.Core.Agent;
using Thoth.Core.Memory;
using Thoth.Core.Tools;

namespace Thoth.Core.Planning;

public sealed record AgentPlanningContext(
    AgentRequest Request,
    IReadOnlyList<MemoryRecord> Memories,
    IReadOnlyList<IAgentTool> Tools);
