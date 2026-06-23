namespace Thoth.Core.Tools;

public sealed record ToolInvocation(string ToolName, IReadOnlyDictionary<string, string?> Arguments)
{
    public string GetString(string name, string defaultValue = "")
    {
        return Arguments.TryGetValue(name, out var value) && value is not null ? value : defaultValue;
    }

    public int GetInt(string name, int defaultValue)
    {
        var value = GetString(name);
        return int.TryParse(value, out var number) ? number : defaultValue;
    }

    public bool GetBool(string name, bool defaultValue)
    {
        var value = GetString(name);
        return bool.TryParse(value, out var boolean) ? boolean : defaultValue;
    }
}
