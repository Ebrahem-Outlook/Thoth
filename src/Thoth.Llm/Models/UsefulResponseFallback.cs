using System.Text.RegularExpressions;
using Thoth.Core.Chat;
using Thoth.Core.Understanding;

namespace Thoth.Llm.Models;

internal static class UsefulResponseFallback
{
    public static AssistantResponse CreateDirectReply(string text, bool hasImage)
    {
        var trimmed = text.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new AssistantResponse(AssistantResponseKind.Clarification, "Send me the request you want Thoth to handle.");
        }

        if (hasImage)
        {
            return new AssistantResponse(
                AssistantResponseKind.CapabilityLimitation,
                ContainsArabic(trimmed)
                    ? "\u0623\u0642\u062f\u0631 \u0623\u0633\u062c\u0644 \u0627\u0644\u0635\u0648\u0631\u0629 \u0643\u0645\u0631\u0641\u0642\u060c \u0644\u0643\u0646 \u0627\u0644\u0646\u0645\u0648\u0630\u062c \u0627\u0644\u0645\u062d\u0644\u064a \u0627\u0644\u062d\u0627\u0644\u064a \u0644\u0633\u0647 \u0645\u0634 \u0645\u0624\u0647\u0644 \u0644\u0641\u0647\u0645 \u0627\u0644\u0635\u0648\u0631 \u0628\u062f\u0642\u0629. \u0627\u0648\u0635\u0641\u0647\u0627 \u0644\u064a \u0648\u0647\u0633\u0627\u0639\u062f\u0643."
                    : "I can keep the image as an attachment, but the current local model is not qualified for reliable image understanding yet. Describe what you need from it and I will help.");
        }

        if (LooksLikeCapabilityQuestion(trimmed))
        {
            return new AssistantResponse(
                AssistantResponseKind.DirectAnswer,
                ContainsArabic(trimmed)
                    ? "\u0623\u0642\u062f\u0631 \u0623\u0631\u062f \u0639\u0644\u0649 \u0627\u0644\u0634\u0627\u062a\u060c \u0623\u0637\u0644\u0628 \u062a\u0648\u0636\u064a\u062d \u0644\u0648 \u0627\u0644\u0637\u0644\u0628 \u0646\u0627\u0642\u0635\u060c \u0623\u0643\u062a\u0628 \u062d\u0644\u0648\u0644 \u0628\u0631\u0645\u062c\u064a\u0629 \u0628\u0633\u064a\u0637\u0629 \u0644\u0644\u062d\u0627\u0644\u0627\u062a \u0627\u0644\u0645\u062f\u0639\u0648\u0645\u0629\u060c \u0648\u0623\u0633\u062a\u062e\u062f\u0645 \u0623\u062f\u0648\u0627\u062a \u0627\u0644\u0645\u0634\u0631\u0648\u0639 \u0623\u0648 \u0627\u0644\u0648\u064a\u0628 \u0644\u0645\u0627 \u062a\u0637\u0644\u0628 \u062f\u0647 \u0628\u0648\u0636\u0648\u062d."
                    : "I can chat, ask for missing details, generate small supported code answers, inspect the repository when tools are explicitly needed, and run web research when you ask for current outside information.");
        }

        if (IsCasual(trimmed))
        {
            return new AssistantResponse(
                AssistantResponseKind.DirectAnswer,
                ContainsArabic(trimmed)
                    ? "\u0623\u064a\u0648\u0647\u060c \u0641\u0627\u0647\u0645\u0643. \u0627\u0628\u0639\u062a \u0627\u0644\u0645\u0637\u0644\u0648\u0628 \u0628\u0648\u0636\u0648\u062d \u0648\u0647\u0631\u062f \u0639\u0644\u064a\u0643 \u0645\u0628\u0627\u0634\u0631\u0629."
                    : "I’m with you. Send the exact thing you want and I’ll answer directly.");
        }

        if (LooksLikeSelfAssessment(trimmed))
        {
            return new AssistantResponse(
                AssistantResponseKind.DirectAnswer,
                "I am more useful when the request is clear and tools are available, but I should not claim real intelligence until a local checkpoint is trained and passes evaluation.");
        }

        if (CodeRequestAnalyzer.LooksLikeCodeRequest(trimmed))
        {
            return CreateCodeReply(trimmed);
        }

        return new AssistantResponse(
            AssistantResponseKind.DirectAnswer,
            ContainsArabic(trimmed)
                ? "\u0641\u0627\u0647\u0645. \u0627\u0628\u0639\u062a \u0627\u0644\u0647\u062f\u0641 \u0623\u0648 \u0627\u0644\u0645\u0644\u0641 \u0627\u0644\u0645\u0637\u0644\u0648\u0628\u060c \u0648\u0647\u0631\u062f \u0628\u0625\u062c\u0627\u0628\u0629 \u0646\u0638\u064a\u0641\u0629 \u0645\u0646 \u063a\u064a\u0631 \u062a\u0641\u0627\u0635\u064a\u0644 \u062f\u0627\u062e\u0644\u064a\u0629."
                : "Got it. Send the concrete goal or file and I will answer cleanly without internal diagnostics.");
    }

    private static AssistantResponse CreateCodeReply(string text)
    {
        var frame = CodeRequestAnalyzer.Analyze(text);
        var language = frame.Language is null ? null : ProgrammingLanguageDetector.DisplayName(frame.Language.Value);
        var arabic = ContainsArabic(text);

        if (frame.IsRepositoryBound)
        {
            return new AssistantResponse(
                AssistantResponseKind.Clarification,
                arabic
                    ? "\u062f\u0647 \u0634\u0643\u0644\u0647 \u0637\u0644\u0628 \u0645\u0631\u062a\u0628\u0637 \u0628\u0627\u0644\u0645\u0634\u0631\u0648\u0639. \u0634\u063a\u0644 \u0627\u0644\u0623\u062f\u0648\u0627\u062a \u0623\u0648 \u062d\u062f\u062f \u0627\u0644\u0645\u0644\u0641 \u0648\u0627\u0644\u062a\u063a\u064a\u064a\u0631 \u0627\u0644\u0645\u0637\u0644\u0648\u0628 \u0648\u0647\u0641\u062d\u0635\u0647."
                    : "This looks repository-bound. Enable tools or name the exact file and change, and I will inspect it before editing.");
        }

        if (frame.Behavior == "calculator" && frame.Language == ProgrammingLanguage.CSharp)
        {
            return new AssistantResponse(AssistantResponseKind.DirectAnswer, BuildCSharpCalculator(text));
        }

        if (frame.Behavior == "calculator" && frame.Language == ProgrammingLanguage.TypeScript)
        {
            return new AssistantResponse(AssistantResponseKind.DirectAnswer, BuildTypeScriptCalculator(text));
        }

        if (frame.MissingRequiredDetails.Count > 0)
        {
            var display = language ?? (arabic ? "\u0627\u0644\u0644\u063a\u0629 \u0627\u0644\u0645\u0637\u0644\u0648\u0628\u0629" : "the requested language");
            var content = arabic
                ? $"\u0623\u0643\u064a\u062f. \u0627\u0644\u0645\u064a\u062b\u0648\u062f \u0641\u064a {display} \u0645\u0637\u0644\u0648\u0628 \u0645\u0646\u0647\u0627 \u062a\u0639\u0645\u0644 \u0625\u064a\u0647\u061f \u0627\u0628\u0639\u062a \u0627\u0644\u0645\u062f\u062e\u0644\u0627\u062a\u060c \u0627\u0644\u0646\u0627\u062a\u062c \u0627\u0644\u0645\u062a\u0648\u0642\u0639\u060c \u0648\u0623\u064a \u0642\u0648\u0627\u0639\u062f validation \u0645\u0647\u0645\u0629\u060c \u0648\u0623\u0646\u0627 \u0623\u0643\u062a\u0628\u0647\u0627 \u0643\u0627\u0645\u0644\u0629."
                : $"Sure. What should the {display} {frame.ArtifactKind ?? "function"} do? Send the inputs, expected output, and any important validation rules, and I will write it cleanly.";
            return new AssistantResponse(AssistantResponseKind.Clarification, content, frame.MissingRequiredDetails);
        }

        return new AssistantResponse(
            AssistantResponseKind.CapabilityLimitation,
            arabic
                ? "\u0627\u0644\u0646\u0645\u0648\u0630\u062c \u0627\u0644\u0645\u062d\u0644\u064a \u0627\u0644\u062d\u0627\u0644\u064a \u0645\u0634 \u0645\u0624\u0647\u0644 \u064a\u0648\u0644\u062f \u0643\u0648\u062f \u0639\u0627\u0645 \u0628\u062b\u0642\u0629 \u0645\u0646 \u063a\u064a\u0631 \u0645\u0648\u0627\u0635\u0641\u0627\u062a \u0623\u062f\u0642."
                : "The current local fallback is not qualified to synthesize arbitrary code safely without a tighter specification.");
    }

    private static string BuildCSharpCalculator(string text)
    {
        var methodName = InferMethodName(text, "Calculate");
        return string.Join(Environment.NewLine,
            "Here is a small C# calculator method:",
            "",
            "```csharp",
            $"public static decimal {methodName}(decimal left, decimal right, char operation)",
            "{",
            "    return operation switch",
            "    {",
            "        '+' => left + right,",
            "        '-' => left - right,",
            "        '*' => left * right,",
            "        '/' when right != 0 => left / right,",
            "        '/' => throw new DivideByZeroException(\"Cannot divide by zero.\"),",
            "        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, \"Unsupported calculator operation.\")",
            "    };",
            "}",
            "```");
    }

    private static string BuildTypeScriptCalculator(string text)
    {
        var methodName = InferMethodName(text, "calculate");
        methodName = char.ToLowerInvariant(methodName[0]) + methodName[1..];
        return string.Join(Environment.NewLine,
            "Here is a small TypeScript calculator function:",
            "",
            "```typescript",
            $"export function {methodName}(left: number, right: number, operation: '+' | '-' | '*' | '/'): number {{",
            "  switch (operation) {",
            "    case '+': return left + right;",
            "    case '-': return left - right;",
            "    case '*': return left * right;",
            "    case '/':",
            "      if (right === 0) throw new Error('Cannot divide by zero.');",
            "      return left / right;",
            "    default:",
            "      throw new Error(`Unsupported calculator operation: ${operation}`);",
            "  }",
            "}",
            "```");
    }

    private static string InferMethodName(string text, string fallback)
    {
        var explicitName = Regex.Match(text, @"(?:named|called|method\s+name|function\s+name|name|\u0627\u0633\u0645)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.IgnoreCase);
        if (!explicitName.Success)
        {
            return fallback;
        }

        var parts = Regex.Matches(explicitName.Groups["name"].Value, @"[A-Za-z0-9]+")
            .Select(match => match.Value)
            .Where(part => part.Length > 0)
            .ToArray();
        return parts.Length == 0
            ? fallback
            : string.Concat(parts.Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static bool LooksLikeCapabilityQuestion(string text) =>
        ContainsAny(text.ToLowerInvariant(), "what can you do", "capabilities", "who are you") ||
        ContainsAny(text, "\u062a\u0642\u062f\u0631 \u062a\u0639\u0645\u0644 \u0627\u064a\u0647", "\u0628\u062a\u0639\u0645\u0644 \u0627\u064a\u0647", "\u0627\u0646\u062a \u0645\u064a\u0646");

    private static bool IsCasual(string text)
    {
        var lower = text.ToLowerInvariant().Trim().Trim('.', '!', '?');
        return lower is "hi" or "hello" or "hey" or "yo" or "sup" or "thanks" or "thank you" ||
               ContainsAny(lower, "do you understand me", "understand me", "are you following", "got me") ||
               ContainsAny(text, "\u0627\u0647\u0644\u0627", "\u0623\u0647\u0644\u0627", "\u0645\u0631\u062d\u0628\u0627", "\u0633\u0644\u0627\u0645", "\u0627\u0632\u064a\u0643", "\u0634\u0643\u0631\u0627", "\u0627\u0646\u062a \u0641\u0627\u0647\u0645\u0646\u064a", "\u0623\u0646\u062a \u0641\u0627\u0647\u0645\u0646\u064a", "\u0641\u0627\u0647\u0645\u0646\u064a", "\u0641\u0647\u0645\u062a\u0646\u064a");
    }

    private static bool LooksLikeSelfAssessment(string text)
    {
        var lower = text.ToLowerInvariant();
        return ContainsAny(lower, "do you think you are smarter", "are you smarter", "are you intelligent", "do you think") ||
               ContainsAny(text, "\u0627\u0646\u062a \u0627\u0630\u0643\u0649", "\u0628\u0642\u064a\u062a \u0627\u0630\u0643\u0649", "\u0627\u0646\u062a \u0630\u0643\u064a");
    }

    private static bool ContainsArabic(string text) => text.Any(c => c >= 0x0600 && c <= 0x06FF);

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
