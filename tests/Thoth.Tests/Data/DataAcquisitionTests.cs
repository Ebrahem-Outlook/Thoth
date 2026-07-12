using Thoth.Data.Acquisition;
using Thoth.Data.Synthetic;
using System.Text.Json;

namespace Thoth.Tests.Data;

public sealed class DataAcquisitionTests
{
    [Theory]
    [InlineData("arwiki")]
    [InlineData("simplewiki")]
    [InlineData("mdn-content")]
    [InlineData("oasst1")]
    [InlineData("curated-code")]
    [InlineData("owned-synthetic")]
    public void Catalog_ContainsRequiredSourcePlans(string sourceId)
    {
        var source = AcquisitionPlanCatalog.Resolve(sourceId);

        Assert.Equal(sourceId, source.SourceId);
        Assert.False(string.IsNullOrWhiteSpace(source.OfficialUrl));
        Assert.False(string.IsNullOrWhiteSpace(source.LicenseSpdx));
        Assert.NotEmpty(source.RequiredApprovalFacts);
        Assert.NotEmpty(source.Steps);
    }

    [Fact]
    public void ApprovalPolicy_RequiresConfirmationForLargeOrUnknownDownloads()
    {
        var source = AcquisitionPlanCatalog.Resolve("arwiki");
        var policy = new DownloadApprovalPolicy();

        var unknown = policy.Evaluate(source, null, expectedExtractedBytes: null, freeDiskBytes: 10L * 1024 * 1024 * 1024);
        var large = policy.Evaluate(source, 3L * 1024 * 1024 * 1024, 4L * 1024 * 1024 * 1024, 10L * 1024 * 1024 * 1024);
        var small = policy.Evaluate(source, 128L * 1024 * 1024, 256L * 1024 * 1024, 10L * 1024 * 1024 * 1024);

        Assert.True(unknown.RequiresExplicitConfirmation);
        Assert.True(large.RequiresExplicitConfirmation);
        Assert.False(small.RequiresExplicitConfirmation);
    }

    [Fact]
    public void OwnedSyntheticGenerator_IsDeterministicAndVerifierBacked()
    {
        var generator = new OwnedSyntheticInstructionGenerator();

        var first = generator.Generate(5, seed: 19);
        var second = generator.Generate(5, seed: 19);

        Assert.Equal(
            JsonSerializer.Serialize(first, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            JsonSerializer.Serialize(second, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.All(first, example =>
        {
            Assert.Equal("passed", example.Verifier.Status);
            Assert.Equal("owned-synthetic", example.Provenance["type"]);
            Assert.Contains(example.Messages, message => message.Role == "user");
            Assert.Contains(example.Messages, message => message.Role == "assistant");
        });
    }
}
