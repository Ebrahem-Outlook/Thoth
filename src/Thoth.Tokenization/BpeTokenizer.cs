using System.Text;
using System.Text.Json;

namespace Thoth.Tokenization;

public sealed class BpeTokenizer : ITextTokenizer
{
    public const int ArtifactVersion = 1;
    public const int SpecialTokenCount = 4;
    private const int ByteSymbolCount = 256;

    private readonly IReadOnlyList<BpeMerge> merges;
    private readonly Dictionary<(int Left, int Right), int> mergeRanks;
    private readonly Dictionary<int, byte[]> expansionCache = new();

    public BpeTokenizer(IReadOnlyList<BpeMerge> merges)
    {
        this.merges = merges.ToArray();
        mergeRanks = new Dictionary<(int Left, int Right), int>(this.merges.Count);
        for (var index = 0; index < this.merges.Count; index++)
        {
            mergeRanks[(this.merges[index].Left, this.merges[index].Right)] = ByteSymbolCount + index;
        }
    }

    public int VocabularySize => SpecialTokenCount + ByteSymbolCount + merges.Count;

    public int PaddingTokenId => 0;

    public int BeginningOfSequenceTokenId => 1;

    public int EndOfSequenceTokenId => 2;

    public int SeparatorTokenId => 3;

    public IReadOnlyList<BpeMerge> Merges => merges;

    public IReadOnlyList<int> Encode(
        string text,
        bool addBeginningOfSequence = false,
        bool addEndOfSequence = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        var symbols = Encoding.UTF8.GetBytes(text).Select(value => (int)value).ToList();
        foreach (var merge in merges)
        {
            ReplacePair(symbols, merge.Left, merge.Right, merge.Symbol);
        }

        var tokens = new List<int>(symbols.Count + 2);
        if (addBeginningOfSequence)
        {
            tokens.Add(BeginningOfSequenceTokenId);
        }

        tokens.AddRange(symbols.Select(symbol => SpecialTokenCount + symbol));

        if (addEndOfSequence)
        {
            tokens.Add(EndOfSequenceTokenId);
        }

        return tokens;
    }

    public string Decode(IEnumerable<int> tokenIds, bool skipSpecialTokens = true)
    {
        ArgumentNullException.ThrowIfNull(tokenIds);
        var bytes = new List<byte>();
        foreach (var tokenId in tokenIds)
        {
            if (tokenId >= SpecialTokenCount && tokenId < VocabularySize)
            {
                bytes.AddRange(Expand(tokenId - SpecialTokenCount));
                continue;
            }

            if (!skipSpecialTokens && tokenId == SeparatorTokenId)
            {
                bytes.Add((byte)'\n');
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    public static BpeTokenizer Train(IEnumerable<string> corpus, int targetVocabularySize = 8_000)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        var sequences = corpus
            .Select(text => Encoding.UTF8.GetBytes(text ?? string.Empty).Select(value => (int)value).ToList())
            .Where(sequence => sequence.Count > 0)
            .ToList();

        if (sequences.Count == 0)
        {
            throw new InvalidDataException("Cannot train a BPE tokenizer on an empty corpus.");
        }

        var maximumMerges = Math.Max(0, targetVocabularySize - SpecialTokenCount - ByteSymbolCount);
        var merges = new List<BpeMerge>(maximumMerges);

        for (var mergeIndex = 0; mergeIndex < maximumMerges; mergeIndex++)
        {
            var counts = CountPairs(sequences);
            if (counts.Count == 0)
            {
                break;
            }

            var best = counts
                .Where(item => item.Value > 1)
                .OrderByDescending(item => item.Value)
                .ThenBy(item => item.Key.Left)
                .ThenBy(item => item.Key.Right)
                .FirstOrDefault();

            if (best.Value <= 1)
            {
                break;
            }

            var symbol = ByteSymbolCount + merges.Count;
            merges.Add(new BpeMerge(best.Key.Left, best.Key.Right, symbol));
            foreach (var sequence in sequences)
            {
                ReplacePair(sequence, best.Key.Left, best.Key.Right, symbol);
            }
        }

        return new BpeTokenizer(merges);
    }

    public static async Task<BpeTokenizer> TrainFromFilesAsync(
        string path,
        int targetVocabularySize = 8_000,
        CancellationToken cancellationToken = default)
    {
        var texts = new List<string>();
        foreach (var file in EnumerateTextFiles(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            texts.Add(await File.ReadAllTextAsync(file, cancellationToken));
        }

        return Train(texts, targetVocabularySize);
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var artifactPath = ResolveArtifactPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        var artifact = new BpeTokenizerArtifact(
            ArtifactVersion,
            "bpe-v1",
            VocabularySize,
            merges);

        await using var stream = new FileStream(artifactPath, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(
            stream,
            artifact,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true },
            cancellationToken);
    }

    public static async Task<BpeTokenizer> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var artifactPath = ResolveArtifactPath(path);
        await using var stream = new FileStream(artifactPath, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous);
        var artifact = await JsonSerializer.DeserializeAsync<BpeTokenizerArtifact>(
            stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            cancellationToken);

        if (artifact is null || artifact.Version != ArtifactVersion)
        {
            throw new InvalidDataException("Unsupported BPE tokenizer artifact.");
        }

        return new BpeTokenizer(artifact.Merges);
    }

    private byte[] Expand(int symbol)
    {
        if (symbol < 0 || symbol >= ByteSymbolCount + merges.Count)
        {
            return [];
        }

        if (symbol < ByteSymbolCount)
        {
            return [(byte)symbol];
        }

        if (expansionCache.TryGetValue(symbol, out var cached))
        {
            return cached;
        }

        var merge = merges[symbol - ByteSymbolCount];
        var left = Expand(merge.Left);
        var right = Expand(merge.Right);
        var bytes = new byte[left.Length + right.Length];
        Buffer.BlockCopy(left, 0, bytes, 0, left.Length);
        Buffer.BlockCopy(right, 0, bytes, left.Length, right.Length);
        expansionCache[symbol] = bytes;
        return bytes;
    }

    private static Dictionary<(int Left, int Right), int> CountPairs(IReadOnlyList<List<int>> sequences)
    {
        var counts = new Dictionary<(int Left, int Right), int>();
        foreach (var sequence in sequences)
        {
            for (var index = 0; index + 1 < sequence.Count; index++)
            {
                var pair = (sequence[index], sequence[index + 1]);
                counts[pair] = counts.TryGetValue(pair, out var count) ? count + 1 : 1;
            }
        }

        return counts;
    }

    private static void ReplacePair(List<int> sequence, int left, int right, int symbol)
    {
        for (var index = 0; index + 1 < sequence.Count;)
        {
            if (sequence[index] == left && sequence[index + 1] == right)
            {
                sequence[index] = symbol;
                sequence.RemoveAt(index + 1);
                index++;
                continue;
            }

            index++;
        }
    }

    private static string ResolveArtifactPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (Path.HasExtension(fullPath))
        {
            return fullPath;
        }

        return Path.Combine(fullPath, "tokenizer.json");
    }

    private static IEnumerable<string> EnumerateTextFiles(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            yield return fullPath;
            yield break;
        }

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Tokenizer training path does not exist: {fullPath}");
        }

        foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories)
                     .Where(file => !IsSkipped(file))
                     .OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
        {
            yield return file;
        }
    }

    private static bool IsSkipped(string file)
    {
        var name = Path.GetFileName(file);
        if (name.StartsWith('.'))
        {
            return true;
        }

        var extension = Path.GetExtension(file);
        return extension.Equals(".bin", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ||
               file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   .Any(part => part is "bin" or "obj" or ".git" or "node_modules" or "dist");
    }
}

public sealed record BpeMerge(int Left, int Right, int Symbol);

public sealed record BpeTokenizerArtifact(
    int Version,
    string Tokenizer,
    int VocabularySize,
    IReadOnlyList<BpeMerge> Merges);

