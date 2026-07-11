using System.Text;
using Thoth.Tokenization;

namespace Thoth.Training;

public static class CorpusLoader
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".cs", ".csproj", ".sln", ".json", ".jsonc", ".yaml", ".yml",
        ".ts", ".tsx", ".js", ".jsx", ".html", ".css", ".scss", ".sql", ".xml", ".py",
        ".java", ".go", ".rs", ".sh", ".ps1"
    };

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", "bin", "obj", "node_modules", "dist", "coverage", "data"
    };

    public static async Task<int[]> LoadTokensAsync(
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

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (tokens.Count > 0)
            {
                tokens.Add(tokenizer.SeparatorTokenId);
            }

            tokens.Add(tokenizer.BeginningOfSequenceTokenId);
            tokens.AddRange(tokenizer.Encode(Normalize(text)));
            tokens.Add(tokenizer.EndOfSequenceTokenId);
        }

        if (tokens.Count < 3)
        {
            throw new InvalidDataException("The corpus did not contain enough readable text to train.");
        }

        return tokens.ToArray();
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
                if (info.Length > 0 && info.Length <= maxFileBytes && SupportedExtensions.Contains(info.Extension))
                {
                    yield return file;
                }
            }
        }
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
}
