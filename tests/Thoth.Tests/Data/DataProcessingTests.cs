using Thoth.Data.Processing;

namespace Thoth.Tests.Data;

public sealed class DataProcessingTests
{
    [Fact]
    public void TextNormalizer_RemovesInvalidControlsAndPreservesCodeIndentation()
    {
        const string text = "Title\r\n\r\n\r\nhello\u0000   world\n```csharp\n    var x = 1;\n```\n";

        var normalized = new TextNormalizer().Normalize(text);

        Assert.DoesNotContain('\u0000', normalized);
        Assert.DoesNotContain("\r", normalized);
        Assert.Contains("hello world", normalized);
        Assert.Contains("    var x = 1;", normalized);
        Assert.DoesNotContain("\n\n\n\n", normalized);
    }

    [Fact]
    public void QualityAnalyzer_RejectsSecretsAndRepeatedBoilerplate()
    {
        var text = string.Join('\n', Enumerable.Repeat("same line same line", 12)) +
                   "\napi_key=sk-abcdefghijklmnopqrstuvwxyz123456";

        var report = new DocumentQualityAnalyzer().Analyze(text);

        Assert.False(report.Accepted);
        Assert.True(report.ContainsSecrets);
        Assert.Contains("contains_secrets", report.RejectionReasons);
        Assert.Contains("repeated_lines", report.RejectionReasons);
    }

    [Fact]
    public void Deduplicator_RejectsExactNormalizedAndNearDuplicates()
    {
        var normalizer = new TextNormalizer();
        var deduper = new DocumentDeduplicator();
        const string first = "This document explains local model training with deterministic manifests and careful split assignment.";
        const string near = "This document explains local model training with deterministic manifests and careful split assignment.";

        var accepted = deduper.InspectAndRemember(first, normalizer.Normalize(first));
        var duplicate = deduper.InspectAndRemember(first, normalizer.Normalize(first));
        var nearDuplicate = deduper.InspectAndRemember(near + " Extra.", normalizer.Normalize(near + " Extra."));

        Assert.True(accepted.Accepted);
        Assert.False(duplicate.Accepted);
        Assert.Equal("exact_duplicate", duplicate.RejectionReason);
        Assert.False(nearDuplicate.Accepted);
        Assert.Equal("near_duplicate", nearDuplicate.RejectionReason);
    }

    [Fact]
    public void StableSplitAssigner_IsDeterministicByGroupKey()
    {
        var assigner = new StableSplitAssigner(seed: 7);

        var first = assigner.Assign("repository:dotnet/runtime");
        var second = assigner.Assign("repository:dotnet/runtime");
        var other = assigner.Assign("repository:angular/angular");

        Assert.Equal(first, second);
        Assert.Contains(first, ["train", "validation", "test"]);
        Assert.Contains(other, ["train", "validation", "test"]);
    }
}
