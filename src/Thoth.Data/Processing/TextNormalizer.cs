using System.Text;
using System.Text.RegularExpressions;

namespace Thoth.Data.Processing;

public sealed class TextNormalizer(TextNormalizationOptions? options = null)
{
    private readonly TextNormalizationOptions options = options ?? new TextNormalizationOptions();

    public string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        normalized = RemoveInvalidControlCharacters(normalized);
        if (options.NormalizeToNfc)
        {
            normalized = normalized.Normalize(NormalizationForm.FormC);
        }

        return options.CollapseWhitespaceOutsideCode
            ? CollapseOutsideCodeFences(normalized, options.MaximumConsecutiveBlankLines)
            : normalized;
    }

    private static string RemoveInvalidControlCharacters(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character == '\n' || character == '\t' || !char.IsControl(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string CollapseOutsideCodeFences(string text, int maximumConsecutiveBlankLines)
    {
        var lines = text.Split('\n');
        var builder = new StringBuilder(text.Length);
        var inCodeFence = false;
        var blankLines = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine;
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCodeFence = !inCodeFence;
                blankLines = 0;
                builder.Append(line.TrimEnd()).Append('\n');
                continue;
            }

            if (!inCodeFence)
            {
                line = Regex.Replace(line.Trim(), @"[ \t]{2,}", " ");
                if (line.Length == 0)
                {
                    blankLines++;
                    if (blankLines > maximumConsecutiveBlankLines)
                    {
                        continue;
                    }
                }
                else
                {
                    blankLines = 0;
                }
            }

            builder.Append(line.TrimEnd()).Append('\n');
        }

        return builder.ToString().TrimEnd('\n');
    }
}
