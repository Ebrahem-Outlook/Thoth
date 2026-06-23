namespace Thoth.Core.Tools;

public sealed record ToolResult(
    string ToolName,
    bool Succeeded,
    string Content,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static ToolResult Success(
        string toolName,
        string content,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(toolName, true, content, metadata ?? new Dictionary<string, string>());

    public static ToolResult Failure(
        string toolName,
        string content,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(toolName, false, content, metadata ?? new Dictionary<string, string>());
}
