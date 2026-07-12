using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Thoth.Tokenization;

namespace Thoth.Training;

public static class CorpusLoader
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".cs", ".csproj", ".sln", ".json", ".jsonc", ".jsonl", ".yaml", ".yml",
        ".ts", ".tsx", ".js", ".jsx", ".html", ".css", ".scss", ".sql", ".xml", ".py",
        ".java", ".go", ".rs", ".sh", ".ps1"
    };

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", "bin", "obj", "node_modules", "dist", "coverage",
        "memory", "uploads", "models", "checkpoints", "tokenizers", "artifacts", "reports"
    };

    private static readonly HashSet<string> SkippedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".db", ".sqlite", ".sqlite3", ".bin", ".dll", ".exe", ".pdb", ".png", ".jpg", ".jpeg",
        ".gif", ".webp", ".pdf", ".zip", ".7z", ".rar"
    };

    public static async Task<int[]> LoadTokensAsync(
        string path,
        ITextTokenizer tokenizer,
        int maxFileBytes = 2 * 1024 * 1024,
        CancellationToken cancellationToken = default) =>
        (await LoadCorpusAsync(path, tokenizer, maxFileBytes, cancellationToken)).Tokens;

    public static async Task<CorpusLoadResult> LoadCorpusAsync(
        string path,
        ITextTokenizer tokenizer,
        int maxFileBytes = 2 * 1024 * 1024,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(tokenizer);

        var fullPath = Path.GetFullPath(path);
        var files = File.Exists(fullPath)
            ? new[] { fullPath }
            : Directory.Exists(fullPath)
                ? EnumerateCorpusFiles(fullPath, maxFileBytes).ToArray()
                : throw new FileNotFoundException("Training corpus path was not found.", fullPath);

        var tokens = new List<int>();
        var entries = new List<CorpusManifestEntry>();
        var seenContentHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string text;
            try
            {
                text = await File.ReadAllTextAsync(file, Encoding.UTF8, cancellationToken);
            }
            catch (DecoderFallbackException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            var normalized = Normalize(text);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            var contentHash = Sha256(normalized);
            if (!seenContentHashes.Add(contentHash))
            {
                continue;
            }

            if (tokens.Count > 0)
            {
                tokens.Add(tokenizer.SeparatorTokenId);
            }

            tokens.Add(tokenizer.BeginningOfSequenceTokenId);
            tokens.AddRange(tokenizer.Encode(normalized));
            tokens.Add(tokenizer.EndOfSequenceTokenId);

            var info = new FileInfo(file);
            entries.Add(new CorpusManifestEntry(
                Path.GetRelativePath(fullPath, file),
                InferPartition(fullPath, file),
                info.Length,
                normalized.Length,
                contentHash));
        }

        if (tokens.Count < 3)
        {
            throw new InvalidDataException("The corpus did not contain enough readable text to train.");
        }

        var manifest = new CorpusManifest(
            fullPath,
            DateTimeOffset.UtcNow,
            entries.Count,
            entries.Sum(entry => entry.ByteLength),
            entries.Sum(entry => entry.CharacterCount),
            tokens.Count,
            entries);
        return new CorpusLoadResult(tokens.ToArray(), manifest);
    }

    public static async Task WriteManifestAsync(
        string path,
        CorpusManifest manifest,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(
            stream,
            manifest,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true },
            cancellationToken);
    }

    private static IEnumerable<string> EnumerateCorpusFiles(string root, int maxFileBytes)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<string> children;
            IEnumerable<string> files;
            try
            {
                children = Directory.EnumerateDirectories(directory);
                files = Directory.EnumerateFiles(directory);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children.OrderByDescending(value => value, StringComparer.OrdinalIgnoreCase))
            {
                if (!SkippedDirectories.Contains(Path.GetFileName(child)))
                {
                    pending.Push(child);
                }
            }

            foreach (var file in files.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                var info = new FileInfo(file);
                if (info.Length <= 0 ||
                    info.Length > maxFileBytes ||
                    SkippedExtensions.Contains(info.Extension) ||
                    !SupportedExtensions.Contains(info.Extension) ||
                    LooksGeneratedPreview(info.Name))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

    private static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string InferPartition(string root, string file)
    {
        if (File.Exists(root))
        {
            return "single";
        }

        var relative = Path.GetRelativePath(root, file);
        var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).FirstOrDefault();
        return first?.ToLowerInvariant() switch
        {
            "train" or "pretrain" => "train",
            "validation" or "valid" or "val" => "validation",
            "test" or "tests" => "test",
            "instructions" => "instructions",
            _ => "pretrain"
        };
    }

    private static bool LooksGeneratedPreview(string fileName) =>
        fileName.Contains("preview", StringComparison.OrdinalIgnoreCase) &&
        fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase);
}

public sealed record CorpusLoadResult(
    int[] Tokens,
    CorpusManifest Manifest);

public sealed record CorpusManifest(
    string RootPath,
    DateTimeOffset GeneratedAt,
    int FileCount,
    long TotalBytes,
    long TotalCharacters,
    int TokenCount,
    IReadOnlyList<CorpusManifestEntry> Files);

public sealed record CorpusManifestEntry(
    string RelativePath,
    string Partition,
    long ByteLength,
    long CharacterCount,
    string Sha256);
