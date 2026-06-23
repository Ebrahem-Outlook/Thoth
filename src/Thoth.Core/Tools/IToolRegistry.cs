namespace Thoth.Core.Tools;

public interface IToolRegistry
{
    IReadOnlyList<IAgentTool> List();

    IAgentTool? Find(string name);
}
