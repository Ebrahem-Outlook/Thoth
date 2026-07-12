using System.Security.Cryptography;
using System.Text;

namespace Thoth.Data.Processing;

public sealed record DeduplicationDecision(
    bool Accepted,
    string DocumentHash,
    string NormalizedHash,
    string? RejectionReason);

public sealed class DocumentDeduplicator
{
    private readonly HashSet<string> rawHashes = new(StringComparer.Ordinal);
    private readonly HashSet<string> normalizedHashes = new(StringComparer.Ordinal);
    private readonly List<HashSet<string>> shingleSets = [];

    public DeduplicationDecision InspectAndRemember(string text, string normalizedText)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(normalizedText);
        var rawHash = Sha256(text);
        var normalizedHash = Sha256(normalizedText);

        if (!rawHashes.Add(rawHash))
        {
            return new DeduplicationDecision(false, rawHash, normalizedHash, "exact_duplicate");
        }

        if (!normalizedHashes.Add(normalizedHash))
        {
            return new DeduplicationDecision(false, rawHash, normalizedHash, "normalized_duplicate");
        }

        var shingles = Shingles(normalizedText);
        if (shingles.Count > 0 && shingleSets.Any(existing => Jaccard(existing, shingles) >= 0.90))
        {
            return new DeduplicationDecision(false, rawHash, normalizedHash, "near_duplicate");
        }

        shingleSets.Add(shingles);
        return new DeduplicationDecision(true, rawHash, normalizedHash, null);
    }

    private static HashSet<string> Shingles(string text)
    {
        var words = text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim().ToLowerInvariant())
            .Where(word => word.Length > 0)
            .ToArray();
        var shingles = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index + 4 < words.Length; index++)
        {
            shingles.Add(string.Join(' ', words.AsSpan(index, 5).ToArray()));
        }

        return shingles;
    }

    private static double Jaccard(HashSet<string> left, HashSet<string> right)
    {
        var intersection = left.Count(item => right.Contains(item));
        var union = left.Count + right.Count - intersection;
        return union == 0 ? 0 : intersection / (double)union;
    }

    private static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
