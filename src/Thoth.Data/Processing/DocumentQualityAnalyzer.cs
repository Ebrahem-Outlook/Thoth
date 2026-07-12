using Thoth.Data.Safety;

namespace Thoth.Data.Processing;

public sealed record DocumentQualityReport(
    bool Accepted,
    double ValidCharacterRatio,
    double SymbolRatio,
    double RepeatedLineRatio,
    double Entropy,
    bool ContainsSecrets,
    bool ContainsPii,
    IReadOnlyList<string> RejectionReasons);

public sealed class DocumentQualityAnalyzer(DataSafetyScanner? scanner = null)
{
    private readonly DataSafetyScanner scanner = scanner ?? new DataSafetyScanner();

    public DocumentQualityReport Analyze(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var reasons = new List<string>();
        var safety = scanner.Scan(text);
        var validCharacterRatio = ComputeValidCharacterRatio(text);
        var symbolRatio = ComputeSymbolRatio(text);
        var repeatedLineRatio = ComputeRepeatedLineRatio(text);
        var entropy = ComputeEntropy(text);

        if (text.Trim().Length < 20)
        {
            reasons.Add("too_short");
        }

        if (validCharacterRatio < 0.95)
        {
            reasons.Add("invalid_character_ratio");
        }

        if (symbolRatio > 0.65)
        {
            reasons.Add("mostly_symbols");
        }

        if (repeatedLineRatio > 0.40)
        {
            reasons.Add("repeated_lines");
        }

        if (safety.ContainsSecrets)
        {
            reasons.Add("contains_secrets");
        }

        if (safety.ContainsPii)
        {
            reasons.Add("contains_pii");
        }

        return new DocumentQualityReport(
            reasons.Count == 0,
            validCharacterRatio,
            symbolRatio,
            repeatedLineRatio,
            entropy,
            safety.ContainsSecrets,
            safety.ContainsPii,
            reasons);
    }

    private static double ComputeValidCharacterRatio(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var valid = text.Count(character => character is '\n' or '\t' || !char.IsControl(character));
        return valid / (double)text.Length;
    }

    private static double ComputeSymbolRatio(string text)
    {
        var nonWhitespace = text.Where(character => !char.IsWhiteSpace(character)).ToArray();
        if (nonWhitespace.Length == 0)
        {
            return 1;
        }

        var symbols = nonWhitespace.Count(character => char.IsSymbol(character) || char.IsPunctuation(character));
        return symbols / (double)nonWhitespace.Length;
    }

    private static double ComputeRepeatedLineRatio(string text)
    {
        var lines = text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        if (lines.Length == 0)
        {
            return 1;
        }

        var repeated = lines
            .GroupBy(line => line, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Sum(group => group.Count() - 1);
        return repeated / (double)lines.Length;
    }

    private static double ComputeEntropy(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        return text
            .GroupBy(character => character)
            .Select(group => group.Count() / (double)text.Length)
            .Sum(probability => -probability * Math.Log2(probability));
    }
}
