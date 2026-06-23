namespace Thoth.Core.Tools;

public sealed record ToolParameter(
    string Name,
    string Description,
    bool Required = true,
    string Type = "string");
