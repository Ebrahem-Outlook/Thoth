using System.Globalization;
using System.Text;

namespace Thoth.Cognition.Text;

public static class ArabicTextNormalizer
{
    public static string NormalizeForMatching(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Normalize(NormalizationForm.FormD).ToLowerInvariant();
        var buffer = new char[normalized.Length];
        var index = 0;
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            buffer[index++] = character switch
            {
                '\u0623' or '\u0625' or '\u0622' or '\u0671' => '\u0627',
                '\u0649' => '\u064a',
                '\u0629' => '\u0647',
                '\u0640' => '\0',
                _ => character
            };
        }

        return new string(buffer, 0, index).Replace("\0", string.Empty, StringComparison.Ordinal).Normalize(NormalizationForm.FormC);
    }

    public static bool HasArabic(string text) => text.Any(c => c >= 0x0600 && c <= 0x06FF);

    public static bool ContainsAny(string text, params string[] needles)
    {
        var normalized = NormalizeForMatching(text);
        return needles
            .Select(NormalizeForMatching)
            .Any(needle => normalized.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
