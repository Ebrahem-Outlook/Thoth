using System.Text.RegularExpressions;

namespace Thoth.Core.Understanding;

public enum ProgrammingLanguage
{
    Unknown,
    TypeScript,
    JavaScript,
    CSharp,
    Python,
    Java,
    Go,
    Rust,
    Cpp,
    Sql,
    Html,
    Css,
    Scss
}

public sealed record ProgrammingLanguageMatch(
    ProgrammingLanguage Language,
    string DisplayName,
    double Confidence,
    string MatchedAlias);

public static class ProgrammingLanguageDetector
{
    private static readonly (ProgrammingLanguage Language, string Display, string[] Aliases)[] AliasMap =
    [
        (ProgrammingLanguage.TypeScript, "TypeScript", ["typescript", "type script", ".ts", "\u062a\u0627\u064a\u0628 \u0633\u0643\u0631\u0628\u062a", "\u062a\u0627\u064a\u0628\u0633\u0643\u0631\u064a\u0628\u062a"]),
        (ProgrammingLanguage.JavaScript, "JavaScript", ["javascript", "java script", ".js", "\u062c\u0627\u0641\u0627\u0633\u0643\u0631\u0628\u062a"]),
        (ProgrammingLanguage.Cpp, "C++", ["c++", "cpp", "c plus plus", ".cpp", ".cc", ".cxx", "\u0633\u064a \u0628\u0644\u0633 \u0628\u0644\u0633"]),
        (ProgrammingLanguage.CSharp, "C#", ["c#", "csharp", "c sharp", ".net", "dotnet", "\u0633\u064a \u0634\u0627\u0631\u0628"]),
        (ProgrammingLanguage.Python, "Python", ["python", ".py", "\u0628\u0627\u064a\u062b\u0648\u0646"]),
        (ProgrammingLanguage.Java, "Java", ["java", ".java", "\u062c\u0627\u0641\u0627"]),
        (ProgrammingLanguage.Go, "Go", ["golang", "go", ".go"]),
        (ProgrammingLanguage.Rust, "Rust", ["rust", ".rs"]),
        (ProgrammingLanguage.Sql, "SQL", ["sql", ".sql"]),
        (ProgrammingLanguage.Scss, "SCSS", ["scss", ".scss"]),
        (ProgrammingLanguage.Css, "CSS", ["css", ".css"]),
        (ProgrammingLanguage.Html, "HTML", ["html", ".html"])
    ];

    public static ProgrammingLanguageMatch? Detect(string text)
    {
        foreach (var item in AliasMap)
        {
            foreach (var alias in item.Aliases)
            {
                if (ContainsAlias(text, alias))
                {
                    return new ProgrammingLanguageMatch(item.Language, item.Display, 0.96, alias);
                }
            }
        }

        if (Regex.IsMatch(text, @"(?<![\p{L}\p{N}_])ts(?![\p{L}\p{N}_])", RegexOptions.IgnoreCase))
        {
            return new ProgrammingLanguageMatch(ProgrammingLanguage.TypeScript, "TypeScript", 0.86, "ts");
        }

        if (Regex.IsMatch(text, @"(?<![\p{L}\p{N}_])js(?![\p{L}\p{N}_])", RegexOptions.IgnoreCase))
        {
            return new ProgrammingLanguageMatch(ProgrammingLanguage.JavaScript, "JavaScript", 0.82, "js");
        }

        return null;
    }

    public static string DisplayName(ProgrammingLanguage language) =>
        AliasMap.FirstOrDefault(item => item.Language == language).Display ?? "unknown";

    private static bool ContainsAlias(string text, string alias)
    {
        if (alias.StartsWith(".", StringComparison.Ordinal))
        {
            return text.Contains(alias, StringComparison.OrdinalIgnoreCase);
        }

        if (alias.Contains('+', StringComparison.Ordinal) ||
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
