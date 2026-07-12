using Thoth.Cognition.Text;

namespace Thoth.Cognition.Concepts;

public static class ArithmeticOperationMatcher
{
    public static ArithmeticOperation? Match(string text)
    {
        var normalized = ArabicTextNormalizer.NormalizeForMatching(text);
        if (ContainsAny(normalized, "+", "add", "addition", "sum", "plus", "\u062c\u0645\u0639", "\u0632\u0627\u064a\u062f"))
        {
            return ArithmeticOperation.Add;
        }

        if (ContainsAny(normalized, "-", "subtract", "subtraction", "minus", "\u0637\u0631\u062d", "\u0646\u0627\u0642\u0635"))
        {
            return ArithmeticOperation.Subtract;
        }

        if (ContainsAny(normalized, "*", "multiply", "multiplication", "times", "\u0636\u0631\u0628", "\u0641\u064a"))
        {
            return ArithmeticOperation.Multiply;
        }

        if (ContainsAny(normalized, "/", "divide", "division", "\u0642\u0633\u0645", "\u0642\u0633\u0645\u0647", "\u0642\u0633\u0645\u0629", "\u0642\u0645\u0633\u0647"))
        {
            return ArithmeticOperation.Divide;
        }

        return null;
    }

    public static bool LooksLikeCalculator(string text)
    {
        var normalized = ArabicTextNormalizer.NormalizeForMatching(text);
        return Match(text) is not null ||
               ContainsAny(
                   normalized,
                   "calculator",
                   "calculate",
                   "calc",
                   "arithmetic",
                   "\u062d\u0627\u0633\u0628\u0647",
                   "\u0627\u0644\u0647 \u062d\u0627\u0633\u0628\u0647");
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(ArabicTextNormalizer.NormalizeForMatching(needle), StringComparison.OrdinalIgnoreCase));
}
