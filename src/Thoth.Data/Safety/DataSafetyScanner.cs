using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Thoth.Data.Safety;

public sealed record SafetyScanResult(
    bool ContainsSecrets,
    bool ContainsPii,
    IReadOnlyList<SafetyFinding> Findings)
{
    public bool IsSafeForTraining => !ContainsSecrets && !ContainsPii;
}

public sealed record SafetyFinding(
    string Kind,
    int Start,
    int Length,
    string Redacted);

public sealed class DataSafetyScanner
{
    private static readonly Regex[] SecretPatterns =
    [
        new(@"-----BEGIN (RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bAKIA[0-9A-Z]{16}\b", RegexOptions.Compiled),
        new(@"\bgh[pousr]_[A-Za-z0-9_]{20,}\b", RegexOptions.Compiled),
        new(@"\bsk-[A-Za-z0-9]{20,}\b", RegexOptions.Compiled),
        new(@"(?i)\b(password|passwd|pwd|secret|api[_-]?key|token|connectionstring)\b\s*[:=]\s*['""]?[^'""\s;]{8,}")
    ];

    private static readonly Regex[] PiiPatterns =
    [
        new(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\b(?:\+?\d{1,3}[-. ]?)?(?:\(?\d{3}\)?[-. ]?)\d{3}[-. ]?\d{4}\b", RegexOptions.Compiled),
        new(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b", RegexOptions.Compiled)
    ];

    public SafetyScanResult Scan(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var findings = new List<SafetyFinding>();

        foreach (var pattern in SecretPatterns)
        {
            foreach (Match match in pattern.Matches(text))
            {
                findings.Add(ToFinding("secret", match));
            }
        }

        foreach (var pattern in PiiPatterns)
        {
            foreach (Match match in pattern.Matches(text))
            {
                findings.Add(ToFinding("pii", match));
            }
        }

        foreach (var entropy in FindHighEntropyRuns(text))
        {
            findings.Add(entropy);
        }

        var containsSecrets = findings.Any(finding => finding.Kind is "secret" or "high_entropy_secret");
        var containsPii = findings.Any(finding => finding.Kind == "pii");
        return new SafetyScanResult(containsSecrets, containsPii, findings.OrderBy(f => f.Start).ToArray());
    }

    private static SafetyFinding ToFinding(string kind, Match match) =>
        new(kind, match.Index, match.Length, Redact(kind, match.Index, match.Length));

    private static IEnumerable<SafetyFinding> FindHighEntropyRuns(string text)
    {
        foreach (Match match in Regex.Matches(text, @"[A-Za-z0-9+/=_-]{32,}", RegexOptions.Compiled))
        {
            var entropy = ShannonEntropy(match.Value);
            if (entropy >= 4.25)
            {
                yield return new SafetyFinding(
                    "high_entropy_secret",
                    match.Index,
                    match.Length,
                    Redact("high_entropy_secret", match.Index, match.Length));
            }
        }
    }

    private static double ShannonEntropy(string value)
    {
        if (value.Length == 0)
        {
            return 0;
        }

        return value
            .GroupBy(character => character)
            .Select(group => group.Count() / (double)value.Length)
            .Sum(probability => -probability * Math.Log2(probability));
    }

    private static string Redact(string kind, int start, int length)
    {
        var bytes = Encoding.UTF8.GetBytes($"{kind}:{start}:{length}");
        var hash = Convert.ToHexString(SHA256.HashData(bytes))[..12].ToLowerInvariant();
        return $"{kind}:{hash}:{length}";
    }
}
