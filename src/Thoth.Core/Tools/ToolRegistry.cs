namespace Thoth.Core.Tools;

public sealed class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, IAgentTool> tools = new(StringComparer.OrdinalIgnoreCase);

    public ToolRegistry Register(IAgentTool tool)
    {
        tools[tool.Name] = tool;
        return this;
    }

    public IReadOnlyList<IAgentTool> List() => tools.Values.OrderBy(tool => tool.Name).ToArray();

    public IAgentTool? Find(string name) => tools.TryGetValue(name, out var tool) ? tool : null;
}
