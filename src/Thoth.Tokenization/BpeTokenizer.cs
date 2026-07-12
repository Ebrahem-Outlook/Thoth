using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Thoth.Tokenization;

public sealed class BpeTokenizer : ITextTokenizer
{
    public const int ArtifactVersion = 2;
    public const int ByteSymbolCount = 256;

    private const string TokenizerName = "byte-level-bpe";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly IReadOnlyList<TokenizerSpecialToken> RequiredSpecialTokens =
    [
        new("<PAD>", 0),
        new("<BOS>", 1),
        new("<EOS>", 2),
        new("<USER>", 3),
        new("<ASSISTANT>", 4),
        new("<SYSTEM>", 5),
        new("<TOOL_CALL>", 6),
        new("<TOOL_RESULT>", 7),
        new("<END_TURN>", 8)
    ];

    private static readonly IReadOnlyList<TokenizerSpecialToken> LegacySpecialTokens =
    [
        new("<PAD>", 0),
        new("<BOS>", 1),
        new("<EOS>", 2),
        new("<SEP>", 3)
    ];

    private readonly IReadOnlyList<BpeMerge> merges;
    private readonly IReadOnlyList<TokenizerSpecialToken> specialTokens;
    private readonly Dictionary<string, int> specialTokenIds;
    private readonly Dictionary<int, string> specialTokenTextById;
    private readonly Dictionary<int, byte[]> expansionCache = new();
    private readonly BpeTokenizerTrainingOptions trainingOptions;

    public BpeTokenizer(IReadOnlyList<BpeMerge> merges)
        : this(
            merges,
            RequiredSpecialTokens,
            new BpeTokenizerTrainingOptions(),
            trainingManifestSha256: null)
    {
    }

    private BpeTokenizer(
        IReadOnlyList<BpeMerge> merges,
        IReadOnlyList<TokenizerSpecialToken> specialTokens,
        BpeTokenizerTrainingOptions trainingOptions,
        string? trainingManifestSha256)
    {
        this.specialTokens = ValidateSpecialTokens(specialTokens);
        this.merges = NormalizeMerges(merges);
        this.trainingOptions = trainingOptions;
        TrainingManifestSha256 = trainingManifestSha256;
        specialTokenIds = this.specialTokens.ToDictionary(token => token.Token, token => token.Id, StringComparer.Ordinal);
        specialTokenTextById = this.specialTokens.ToDictionary(token => token.Id, token => token.Token);
    }

    public int VocabularySize => specialTokens.Count + ByteSymbolCount + merges.Count;

    public int PaddingTokenId => specialTokenIds["<PAD>"];

    public int BeginningOfSequenceTokenId => specialTokenIds["<BOS>"];

    public int EndOfSequenceTokenId => specialTokenIds["<EOS>"];

    public int SeparatorTokenId =>
        specialTokenIds.TryGetValue("<END_TURN>", out var endTurn) ? endTurn : specialTokenIds["<SEP>"];

    public IReadOnlyList<BpeMerge> Merges => merges;

    public IReadOnlyList<TokenizerSpecialToken> SpecialTokens => specialTokens;

    public BpeTokenizerTrainingOptions TrainingOptions => trainingOptions;

    public string? TrainingManifestSha256 { get; }

    public int GetSpecialTokenId(string token) =>
        specialTokenIds.TryGetValue(token, out var id)
            ? id
            : throw new KeyNotFoundException($"Unknown special token: {token}");

    public IReadOnlyList<int> Encode(
        string text,
        bool addBeginningOfSequence = false,
        bool addEndOfSequence = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = NormalizeText(text, trainingOptions.NormalizeToNfc);
        var tokens = new List<int>(normalized.Length + 2);

        if (addBeginningOfSequence)
        {
            tokens.Add(BeginningOfSequenceTokenId);
        }

        foreach (var segment in SplitBySpecialTokens(normalized))
        {
            if (segment.SpecialTokenId is { } specialTokenId)
            {
                tokens.Add(specialTokenId);
                continue;
            }

            tokens.AddRange(EncodeOrdinaryText(segment.Text));
        }

        if (addEndOfSequence)
        {
            tokens.Add(EndOfSequenceTokenId);
        }

        return tokens;
    }

    public string Decode(IEnumerable<int> tokenIds, bool skipSpecialTokens = true)
    {
        ArgumentNullException.ThrowIfNull(tokenIds);
        var builder = new StringBuilder();
        var bytes = new List<byte>();

        foreach (var tokenId in tokenIds)
        {
            if (specialTokenTextById.TryGetValue(tokenId, out var specialToken))
            {
                if (!skipSpecialTokens)
                {
                    FlushBytes(builder, bytes);
                    builder.Append(specialToken == "<SEP>" ? "\n" : specialToken);
                }

                continue;
            }

            if (tokenId >= specialTokens.Count && tokenId < VocabularySize)
            {
                bytes.AddRange(Expand(tokenId - specialTokens.Count));
            }
        }

        FlushBytes(builder, bytes);
        return builder.ToString();
    }

    public static BpeTokenizer Train(IEnumerable<string> corpus, int targetVocabularySize = 8_000) =>
        Train(corpus, new BpeTokenizerTrainingOptions(TargetVocabularySize: targetVocabularySize));

    public static BpeTokenizer Train(IEnumerable<string> corpus, BpeTokenizerTrainingOptions? options)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        options ??= new BpeTokenizerTrainingOptions();
        options.Validate();

        var sequences = new List<List<int>>();
        using var manifestHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var index = 0;
        foreach (var text in corpus)
        {
            var normalized = NormalizeText(text ?? string.Empty, options.NormalizeToNfc);
            var bytes = Encoding.UTF8.GetBytes(normalized);
            if (bytes.Length == 0)
            {
                continue;
            }

            sequences.Add(bytes.Select(value => (int)value).ToList());
            AppendManifestLine(manifestHash, $"memory:{index}:{bytes.Length}:{Sha256Hex(bytes)}");
            index++;
        }

        return TrainSequences(sequences, options, Convert.ToHexString(manifestHash.GetHashAndReset()).ToLowerInvariant());
    }

    public static async Task<BpeTokenizer> TrainFromFilesAsync(
        string path,
        int targetVocabularySize = 8_000,
        CancellationToken cancellationToken = default) =>
        await TrainFromFilesAsync(
            path,
            new BpeTokenizerTrainingOptions(TargetVocabularySize: targetVocabularySize),
            cancellationToken);

    public static async Task<BpeTokenizer> TrainFromFilesAsync(
        string path,
        BpeTokenizerTrainingOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        options ??= new BpeTokenizerTrainingOptions();
        options.Validate();

        var fullRoot = Path.GetFullPath(path);
        var sequences = new List<List<int>>();
        using var manifestHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var file in EnumerateTextFiles(fullRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
            if (bytes.Length == 0)
            {
                continue;
            }

            if (options.NormalizeToNfc)
            {
                var normalized = Encoding.UTF8.GetString(bytes).Normalize(NormalizationForm.FormC);
                bytes = Encoding.UTF8.GetBytes(normalized);
            }

            sequences.Add(bytes.Select(value => (int)value).ToList());
            var relativePath = File.Exists(fullRoot)
                ? Path.GetFileName(file)
                : Path.GetRelativePath(fullRoot, file);
            AppendManifestLine(manifestHash, $"file:{relativePath.Replace('\\', '/')}:{bytes.Length}:{Sha256Hex(bytes)}");
        }

        return TrainSequences(sequences, options, Convert.ToHexString(manifestHash.GetHashAndReset()).ToLowerInvariant());
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var package = TokenizerPackagePaths.Resolve(path);
        Directory.CreateDirectory(package.Directory);

        var metadata = new BpeTokenizerMetadata(
            TokenizerName,
            ArtifactVersion,
            trainingOptions.Profile,
            trainingOptions.TargetVocabularySize,
            trainingOptions.MinimumPairFrequency,
            trainingOptions.NormalizeToNfc,
            VocabularySize,
            merges.Count,
            TrainingManifestSha256,
            specialTokens);

        await WriteJsonAtomicAsync(package.VocabJson, BuildVocabulary(), cancellationToken);
        await WriteTextAtomicAsync(package.MergesTxt, BuildMergesText(), cancellationToken);
        await WriteJsonAtomicAsync(package.MetadataJson, metadata, cancellationToken);
        await WriteTextAtomicAsync(
            package.TrainingManifestSha256,
            $"{TrainingManifestSha256 ?? "unknown"}  training-manifest\n",
            cancellationToken);

        var artifactHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["vocab.json"] = await Sha256FileAsync(package.VocabJson, cancellationToken),
            ["merges.txt"] = await Sha256FileAsync(package.MergesTxt, cancellationToken),
            ["tokenizer-metadata.json"] = await Sha256FileAsync(package.MetadataJson, cancellationToken),
            ["training-manifest.sha256"] = await Sha256FileAsync(package.TrainingManifestSha256, cancellationToken)
        };

        var artifact = new BpeTokenizerArtifact
        {
            Version = ArtifactVersion,
            Tokenizer = TokenizerName,
            VocabularySize = VocabularySize,
            ByteSymbolCount = ByteSymbolCount,
            SpecialTokens = specialTokens,
            Merges = merges,
            TargetVocabularySize = trainingOptions.TargetVocabularySize,
            MinimumPairFrequency = trainingOptions.MinimumPairFrequency,
            NormalizeToNfc = trainingOptions.NormalizeToNfc,
            Profile = trainingOptions.Profile,
            TrainingManifestSha256 = TrainingManifestSha256,
            ArtifactSha256 = artifactHashes
        };

        await WriteJsonAtomicAsync(package.TokenizerJson, artifact, cancellationToken);
    }

    public static async Task<BpeTokenizer> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var package = TokenizerPackagePaths.Resolve(path);
        await using var stream = new FileStream(
            package.TokenizerJson,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous);
        var artifact = await JsonSerializer.DeserializeAsync<BpeTokenizerArtifact>(stream, JsonOptions, cancellationToken);

        if (artifact is null || artifact.Version is not 1 and not ArtifactVersion)
        {
            throw new InvalidDataException("Unsupported BPE tokenizer artifact.");
        }

        await VerifyArtifactHashesAsync(package, artifact, cancellationToken);
        var specialTokens = artifact.SpecialTokens is { Count: > 0 }
            ? artifact.SpecialTokens
            : artifact.Version == 1
                ? LegacySpecialTokens
                : RequiredSpecialTokens;

        var options = new BpeTokenizerTrainingOptions(
            artifact.TargetVocabularySize <= 0 ? artifact.VocabularySize : artifact.TargetVocabularySize,
            artifact.MinimumPairFrequency <= 0 ? 2 : artifact.MinimumPairFrequency,
            artifact.NormalizeToNfc,
            string.IsNullOrWhiteSpace(artifact.Profile) ? "loaded" : artifact.Profile);

        return new BpeTokenizer(
            artifact.Merges,
            specialTokens,
            options,
            artifact.TrainingManifestSha256);
    }

    private static BpeTokenizer TrainSequences(
        IReadOnlyList<List<int>> sequences,
        BpeTokenizerTrainingOptions options,
        string trainingManifestSha256)
    {
        if (sequences.Count == 0)
        {
            throw new InvalidDataException("Cannot train a BPE tokenizer on an empty corpus.");
        }

        var maximumMerges = Math.Max(0, options.TargetVocabularySize - RequiredSpecialTokens.Count - ByteSymbolCount);
        var merges = new List<BpeMerge>(maximumMerges);

        for (var mergeIndex = 0; mergeIndex < maximumMerges; mergeIndex++)
        {
            var counts = CountPairs(sequences);
            if (counts.Count == 0)
            {
                break;
            }

            var best = counts
                .Where(item => item.Value >= options.MinimumPairFrequency)
                .OrderByDescending(item => item.Value)
                .ThenBy(item => item.Key.Left)
                .ThenBy(item => item.Key.Right)
                .FirstOrDefault();

            if (best.Value < options.MinimumPairFrequency)
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

        return new BpeTokenizer(merges, RequiredSpecialTokens, options, trainingManifestSha256);
    }

    private IReadOnlyList<int> EncodeOrdinaryText(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var symbols = Encoding.UTF8.GetBytes(text).Select(value => (int)value).ToList();
        foreach (var merge in merges)
        {
            ReplacePair(symbols, merge.Left, merge.Right, merge.Symbol);
        }

        return symbols.Select(symbol => specialTokens.Count + symbol).ToArray();
    }

    private IEnumerable<TokenSegment> SplitBySpecialTokens(string text)
    {
        var start = 0;
        var index = 0;
        while (index < text.Length)
        {
            var matched = MatchSpecialToken(text, index);
            if (matched is null)
            {
                index++;
                continue;
            }

            if (index > start)
            {
                yield return new TokenSegment(text[start..index], null);
            }

            yield return new TokenSegment(matched.Token, matched.Id);
            index += matched.Token.Length;
            start = index;
        }

        if (start < text.Length)
        {
            yield return new TokenSegment(text[start..], null);
        }
    }

    private TokenizerSpecialToken? MatchSpecialToken(string text, int index)
    {
        foreach (var token in specialTokens.OrderByDescending(token => token.Token.Length))
        {
            if (index + token.Token.Length <= text.Length &&
                string.CompareOrdinal(text, index, token.Token, 0, token.Token.Length) == 0)
            {
                return token;
            }
        }

        return null;
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

    private IReadOnlyList<TokenizerVocabularyEntry> BuildVocabulary()
    {
        var entries = new List<TokenizerVocabularyEntry>(VocabularySize);
        entries.AddRange(specialTokens.Select(token => new TokenizerVocabularyEntry(
            token.Id,
            token.Token,
            "special",
            null,
            null)));

        for (var value = 0; value < ByteSymbolCount; value++)
        {
            entries.Add(new TokenizerVocabularyEntry(
                specialTokens.Count + value,
                $"byte:{value:X2}",
                "byte",
                null,
                null));
        }

        entries.AddRange(merges.Select(merge => new TokenizerVocabularyEntry(
            specialTokens.Count + merge.Symbol,
            $"merge:{merge.Symbol}",
            "merge",
            merge.Left,
            merge.Right)));

        return entries;
    }

    private string BuildMergesText()
    {
        var builder = new StringBuilder();
        foreach (var merge in merges)
        {
            builder.Append(merge.Left)
                .Append(' ')
                .Append(merge.Right)
                .Append(' ')
                .Append(merge.Symbol)
                .Append('\n');
        }

        return builder.ToString();
    }

    private static IReadOnlyList<BpeMerge> NormalizeMerges(IReadOnlyList<BpeMerge> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source
            .Select((merge, index) => merge.Symbol == ByteSymbolCount + index
                ? merge
                : merge with { Symbol = ByteSymbolCount + index })
            .ToArray();
    }

    private static IReadOnlyList<TokenizerSpecialToken> ValidateSpecialTokens(IReadOnlyList<TokenizerSpecialToken> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        for (var index = 0; index < tokens.Count; index++)
        {
            if (tokens[index].Id != index)
            {
                throw new InvalidDataException("Tokenizer special token IDs must be zero-based and contiguous.");
            }
        }

        return tokens.ToArray();
    }

    private static string NormalizeText(string text, bool normalizeToNfc) =>
        normalizeToNfc ? text.Normalize(NormalizationForm.FormC) : text;

    private static void FlushBytes(StringBuilder builder, List<byte> bytes)
    {
        if (bytes.Count == 0)
        {
            return;
        }

        builder.Append(Encoding.UTF8.GetString(bytes.ToArray()));
        bytes.Clear();
    }

    private static async Task VerifyArtifactHashesAsync(
        TokenizerPackagePaths package,
        BpeTokenizerArtifact artifact,
        CancellationToken cancellationToken)
    {
        if (artifact.ArtifactSha256.Count == 0)
        {
            return;
        }

        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["vocab.json"] = package.VocabJson,
            ["merges.txt"] = package.MergesTxt,
            ["tokenizer-metadata.json"] = package.MetadataJson,
            ["training-manifest.sha256"] = package.TrainingManifestSha256
        };

        foreach (var expected in artifact.ArtifactSha256)
        {
            if (!paths.TryGetValue(expected.Key, out var filePath))
            {
                continue;
            }

            if (!File.Exists(filePath))
            {
                throw new InvalidDataException($"Tokenizer artifact is missing: {expected.Key}");
            }

            var actual = await Sha256FileAsync(filePath, cancellationToken);
            if (!actual.Equals(expected.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Tokenizer artifact hash mismatch: {expected.Key}");
            }
        }
    }

    private static async Task WriteJsonAtomicAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task WriteTextAtomicAsync(
        string path,
        string value,
        CancellationToken cancellationToken)
    {
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, value, Encoding.UTF8, cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<string> Sha256FileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void AppendManifestLine(IncrementalHash hash, string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        hash.AppendData(bytes);
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

    private sealed record TokenSegment(string Text, int? SpecialTokenId);

    private sealed record TokenizerPackagePaths(
        string Directory,
        string TokenizerJson,
        string VocabJson,
        string MergesTxt,
        string MetadataJson,
        string TrainingManifestSha256)
    {
        public static TokenizerPackagePaths Resolve(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.HasExtension(fullPath)
                ? Path.GetDirectoryName(fullPath)!
                : fullPath;
            var tokenizerJson = Path.HasExtension(fullPath)
                ? fullPath
                : Path.Combine(directory, "tokenizer.json");

            return new TokenizerPackagePaths(
                directory,
                tokenizerJson,
                Path.Combine(directory, "vocab.json"),
                Path.Combine(directory, "merges.txt"),
                Path.Combine(directory, "tokenizer-metadata.json"),
                Path.Combine(directory, "training-manifest.sha256"));
        }
    }
}

public sealed record BpeMerge(int Left, int Right, int Symbol);

public sealed record TokenizerSpecialToken(string Token, int Id);

public sealed record BpeTokenizerProfile(string Name, int VocabularySize);

public static class BpeTokenizerProfiles
{
    public static readonly BpeTokenizerProfile Smoke = new("smoke", 2_048);
    public static readonly BpeTokenizerProfile LaptopPilot = new("laptop-pilot", 8_000);
    public static readonly BpeTokenizerProfile LaptopMax = new("laptop-max", 12_000);

    public static IReadOnlyList<BpeTokenizerProfile> All { get; } =
    [
        Smoke,
        LaptopPilot,
        LaptopMax
    ];

    public static BpeTokenizerProfile Resolve(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return All.FirstOrDefault(profile => profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
               ?? throw new ArgumentOutOfRangeException(nameof(name), $"Unknown tokenizer profile: {name}");
    }
}

public sealed record BpeTokenizerTrainingOptions(
    int TargetVocabularySize = 8_000,
    int MinimumPairFrequency = 2,
    bool NormalizeToNfc = false,
    string Profile = "custom")
{
    public void Validate()
    {
        if (TargetVocabularySize < BpeTokenizer.ByteSymbolCount)
        {
            throw new ArgumentOutOfRangeException(nameof(TargetVocabularySize));
        }

        if (MinimumPairFrequency < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumPairFrequency));
        }
    }
}

public sealed record BpeTokenizerArtifact
{
    public int Version { get; init; }

    public string Tokenizer { get; init; } = "byte-level-bpe";

    public int VocabularySize { get; init; }

    public int ByteSymbolCount { get; init; } = BpeTokenizer.ByteSymbolCount;

    public IReadOnlyList<TokenizerSpecialToken> SpecialTokens { get; init; } = [];

    public IReadOnlyList<BpeMerge> Merges { get; init; } = [];

    public int TargetVocabularySize { get; init; }

    public int MinimumPairFrequency { get; init; }

    public bool NormalizeToNfc { get; init; }

    public string Profile { get; init; } = "custom";

    public string? TrainingManifestSha256 { get; init; }

    public IReadOnlyDictionary<string, string> ArtifactSha256 { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record BpeTokenizerMetadata(
    string Tokenizer,
    int Version,
    string Profile,
    int TargetVocabularySize,
    int MinimumPairFrequency,
    bool NormalizeToNfc,
    int VocabularySize,
    int MergeCount,
    string? TrainingManifestSha256,
    IReadOnlyList<TokenizerSpecialToken> SpecialTokens);

public sealed record TokenizerVocabularyEntry(
    int Id,
    string Token,
    string Kind,
    int? Left,
    int? Right);
