using Thoth.Core.Tools;

namespace Thoth.Core.Sandbox;

public interface IExecutionPolicy
{
    PolicyDecision Authorize(ToolInvocation invocation, ToolContext context);
}
