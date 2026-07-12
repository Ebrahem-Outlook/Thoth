using Thoth.Tokenization;
using Thoth.Training;

namespace Thoth.Tests.Neural;

public sealed class CorpusLoaderTests
{
    [Fact]
    public async Task LoadCorpusAsync_UsesExplicitTrainingDirectoryAndWritesManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "thoth-tests", Guid.NewGuid().ToString("N"), "data", "training");
        Directory.CreateDirectory(Path.Combine(root, "pretrain"));
        Directory.CreateDirectory(Path.Combine(root, "validation"));
        Directory.CreateDirectory(Path.Combine(root, "uploads"));
        Directory.CreateDirectory(Path.Combine(root, "memory"));
        await File.WriteAllTextAsync(Path.Combine(root, "pretrain", "a.txt"), "alpha beta");
        await File.WriteAllTextAsync(Path.Combine(root, "pretrain", "duplicate.txt"), "alpha beta");
        await File.WriteAllTextAsync(Path.Combine(root, "validation", "b.txt"), "\u062b\u0648\u062b \u064a\u0642\u0631\u0623 \u0639\u0631\u0628\u064a");
        await File.WriteAllTextAsync(Path.Combine(root, "uploads", "ignored.txt"), "ignored upload");
        await File.WriteAllTextAsync(Path.Combine(root, "memory", "thoth.sqlite"), "ignored db");

        var result = await CorpusLoader.LoadCorpusAsync(root, new ByteTokenizer());
        var manifestPath = Path.Combine(root, "manifest.json");
        await CorpusLoader.WriteManifestAsync(manifestPath, result.Manifest);

        Assert.True(result.Tokens.Length > 10);
        Assert.Equal(2, result.Manifest.FileCount);
        Assert.Contains(result.Manifest.Files, file => file.Partition == "train");
        Assert.Contains(result.Manifest.Files, file => file.Partition == "validation");
        Assert.DoesNotContain(result.Manifest.Files, file => file.RelativePath.Contains("uploads", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(manifestPath));
    }
}
