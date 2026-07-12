using System.Text.RegularExpressions;

namespace Thoth.Core.Understanding;

public sealed record CodeRequestFrame(
    ProgrammingLanguage? Language,
    string? ArtifactKind,
    string? Behavior,
    IReadOnlyList<string> Inputs,
    string? Output,
    IReadOnlyList<string> ValidationRules,
    bool IsRepositoryBound,
    IReadOnlyList<string> MissingRequiredDetails);

public static class CodeRequestAnalyzer
{
    public static CodeRequestFrame Analyze(string text)
    {
        var language = ProgrammingLanguageDetector.Detect(text)?.Language;
        var lower = text.ToLowerInvariant();
        var artifact = DetectArtifact(lower, text);
        var behavior = DetectBehavior(lower, text);
        IReadOnlyList<string> inputs = behavior == "calculator"
            ? ["left number", "right number", "operation"]
            : Array.Empty<string>();
        var output = behavior == "calculator" ? "calculated numeric result" : null;
        IReadOnlyList<string> validation = behavior == "calculator"
            ? ["division by zero", "unsupported operation"]
            : Array.Empty<string>();
        var repositoryBound = IsRepositoryBound(lower, text);
        var missing = new List<string>();

        if (language is null)
        {
            missing.Add("language");
        }

        if (string.IsNullOrWhiteSpace(artifact))
        {
            missing.Add("artifact kind");
        }

        if (string.IsNullOrWhiteSpace(behavior))
        {
            missing.Add("behavior");
        }

        if (inputs.Count == 0)
        {
            missing.Add("inputs");
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            missing.Add("output");
        }

        return new CodeRequestFrame(language, artifact, behavior, inputs, output, validation, repositoryBound, missing);
    }

    public static bool LooksLikeCodeRequest(string text)
    {
        var lower = text.ToLowerInvariant();
        return ProgrammingLanguageDetector.Detect(text) is not null ||
               ContainsAny(lower, "code", "method", "meethod", "function", "class", "snippet") ||
               ContainsAny(text, "\u0643\u0648\u062f", "\u0645\u064a\u062b\u0648\u062f", "\u0645\u064a\u062b\u062f", "\u062f\u0627\u0644\u0629", "\u0643\u0644\u0627\u0633");
    }

    private static string? DetectArtifact(string lower, string text)
    {
        if (ContainsAny(lower, "method", "meethod", "function") ||
            ContainsAny(text, "\u0645\u064a\u062b\u0648\u062f", "\u0645\u064a\u062b\u062f", "\u062f\u0627\u0644\u0629"))
        {
            return "method/function";
        }

        if (ContainsAny(lower, "class") || text.Contains("\u0643\u0644\u0627\u0633", StringComparison.OrdinalIgnoreCase))
        {
            return "class";
        }

        return null;
    }

    private static string? DetectBehavior(string lower, string text)
    {
        if (ContainsAny(lower, "calculator", "calculate", "calc") ||
            ContainsAny(text, "\u062d\u0627\u0633\u0628\u0629", "\u0622\u0644\u0629 \u062d\u0627\u0633\u0628\u0629", "\u0627\u0644\u0629 \u062d\u0627\u0633\u0628\u0629"))
        {
            return "calculator";
        }

        return null;
    }

    private static bool IsRepositoryBound(string lower, string text) =>
        Regex.IsMatch(text, @"[\w./\\-]+\.(cs|ts|html|scss|json|md|csproj|sln)", RegexOptions.IgnoreCase) ||
        ContainsAny(lower, "repo", "repository", "workspace", "project", "src/", "tests/", "fix", "refactor", "run tests") ||
        ContainsAny(text, "\u0627\u0644\u0645\u0634\u0631\u0648\u0639", "\u0627\u0644\u0645\u0644\u0641", "\u0627\u0644\u0631\u064a\u0628\u0648");

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
