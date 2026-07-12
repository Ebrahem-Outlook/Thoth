using System.Text.RegularExpressions;
using Thoth.Cognition.Text;

namespace Thoth.Cognition.Concepts;

public enum CognitiveProgrammingLanguage
{
    Unknown,
    CSharp,
    TypeScript,
    JavaScript,
    Cpp
}

public enum CodeArtifactKind
{
    Unknown,
    Method,
    Function,
    Class
}

public enum ArithmeticOperation
{
    Add,
    Subtract,
    Multiply,
    Divide
}

public static class CognitiveProgrammingLanguageExtensions
{
    public static string DisplayName(this CognitiveProgrammingLanguage language) => language switch
    {
        CognitiveProgrammingLanguage.CSharp => "C#",
        CognitiveProgrammingLanguage.TypeScript => "TypeScript",
        CognitiveProgrammingLanguage.JavaScript => "JavaScript",
        CognitiveProgrammingLanguage.Cpp => "C++",
        _ => "unknown"
    };

    public static string CodeFence(this CognitiveProgrammingLanguage language) => language switch
    {
        CognitiveProgrammingLanguage.CSharp => "csharp",
        CognitiveProgrammingLanguage.TypeScript => "typescript",
        CognitiveProgrammingLanguage.JavaScript => "javascript",
        CognitiveProgrammingLanguage.Cpp => "cpp",
        _ => "text"
    };
}

public static class ProgrammingLanguageMatcher
{
    private static readonly (CognitiveProgrammingLanguage Language, string[] Aliases)[] AliasMap =
    [
        (CognitiveProgrammingLanguage.Cpp, ["c++", "cpp", "c plus plus", ".cpp", ".cc", ".cxx", "\u0633\u064a \u0628\u0644\u0633 \u0628\u0644\u0633"]),
        (CognitiveProgrammingLanguage.CSharp, ["c#", "csharp", "c sharp", ".net", "dotnet", "\u0633\u064a \u0634\u0627\u0631\u0628"]),
        (CognitiveProgrammingLanguage.TypeScript, ["typescript", "type script", ".ts", "\u062a\u0627\u064a\u0628 \u0633\u0643\u0631\u0628\u062a", "\u062a\u0627\u064a\u0628\u0633\u0643\u0631\u064a\u0628\u062a"]),
        (CognitiveProgrammingLanguage.JavaScript, ["javascript", "java script", ".js", "\u062c\u0627\u0641\u0627\u0633\u0643\u0631\u0628\u062a"])
    ];

    public static CognitiveProgrammingLanguage Detect(string text)
    {
        var normalized = ArabicTextNormalizer.NormalizeForMatching(text);
        foreach (var item in AliasMap)
        {
            foreach (var alias in item.Aliases)
            {
                if (ContainsAlias(normalized, ArabicTextNormalizer.NormalizeForMatching(alias)))
                {
                    return item.Language;
                }
            }
        }

        return CognitiveProgrammingLanguage.Unknown;
    }

    private static bool ContainsAlias(string text, string alias)
    {
        if (alias.StartsWith(".", StringComparison.Ordinal) ||
            alias.Contains('+', StringComparison.Ordinal) ||
            alias.Contains('#', StringComparison.Ordinal))
        {
            return text.Contains(alias, StringComparison.OrdinalIgnoreCase);
        }

        return Regex.IsMatch(
            text,
            $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(alias)}(?![\p{{L}}\p{{N}}_])",
            RegexOptions.IgnoreCase);
    }
}
