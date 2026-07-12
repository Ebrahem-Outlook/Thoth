using Thoth.Cognition.Concepts;
using Thoth.Cognition.Text;

namespace Thoth.Cognition.Tasks;

public static class TaskResponseComposer
{
    public static string CreateClarification(CodeGenerationTask task, string userText)
    {
        var arabic = ArabicTextNormalizer.HasArabic(userText);
        var missing = string.Join(", ", task.MissingSlots);
        var language = task.Language.DisplayName();
        if (arabic)
        {
            return string.Equals(missing, "behavior", StringComparison.OrdinalIgnoreCase)
                ? $"\u062a\u0645\u0627\u0645. \u0627\u0644\u0640 {language} method \u0645\u0637\u0644\u0648\u0628 \u062a\u0639\u0645\u0644 \u0625\u064a\u0647 \u0628\u0627\u0644\u0638\u0628\u0637\u061f"
                : $"\u0646\u0627\u0642\u0635\u0646\u064a \u0627\u0644\u062a\u0641\u0627\u0635\u064a\u0644 \u062f\u064a: {missing}.";
        }

        return string.Equals(missing, "behavior", StringComparison.OrdinalIgnoreCase)
            ? $"Sure. What should the {language} method do?"
            : $"I need these details before writing the code: {missing}.";
    }
}
