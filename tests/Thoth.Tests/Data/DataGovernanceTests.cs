using Thoth.Data.Governance;
using Thoth.Data.Manifests;
using Thoth.Data.Safety;

namespace Thoth.Tests.Data;

public sealed class DataGovernanceTests
{
    [Fact]
    public void LicensePolicy_AllowsPermissiveLicensesAndRejectsUnknownByDefault()
    {
        var policy = LicensePolicy.ConservativeDefault;

        Assert.Equal(LicenseDecisionStatus.Allowed, policy.Evaluate("MIT").Status);
        Assert.Equal(LicenseDecisionStatus.Allowed, policy.Evaluate("Apache-2.0").Status);
        Assert.Equal(LicenseDecisionStatus.ReviewRequired, policy.Evaluate("CC-BY-SA-4.0").Status);
        Assert.Equal(LicenseDecisionStatus.Rejected, policy.Evaluate("GPL-3.0").Status);
        Assert.Equal(LicenseDecisionStatus.Rejected, policy.Evaluate("NOASSERTION").Status);
        Assert.Equal(LicenseDecisionStatus.Rejected, policy.Evaluate(null).Status);
    }

    [Fact]
    public void SafetyScanner_DetectsSecretsAndPiiWithoutLeakingValues()
    {
        const string secret = "sk-abcdefghijklmnopqrstuvwxyz123456";
        const string text = $"email admin@example.com\napi_key={secret}\nserver=192.168.1.10";

        var result = new DataSafetyScanner().Scan(text);

        Assert.True(result.ContainsSecrets);
        Assert.True(result.ContainsPii);
        Assert.Contains(result.Findings, finding => finding.Kind == "secret");
        Assert.Contains(result.Findings, finding => finding.Kind == "pii");
        Assert.DoesNotContain(result.Findings, finding => finding.Redacted.Contains(secret, StringComparison.Ordinal));
        Assert.All(result.Findings, finding => Assert.DoesNotContain("@example.com", finding.Redacted, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ManifestWriter_CreatesRequiredSkeletonFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "thoth-tests", Guid.NewGuid().ToString("N"), "manifests");

        await DataManifestWriter.EnsureSkeletonAsync(root);
        await DataManifestWriter.EnsureSkeletonAsync(root);

        Assert.True(File.Exists(Path.Combine(root, "sources.json")));
        Assert.True(File.Exists(Path.Combine(root, "documents.jsonl")));
        Assert.True(File.Exists(Path.Combine(root, "licenses.json")));
        Assert.True(File.Exists(Path.Combine(root, "dataset-build.json")));
        Assert.True(File.Exists(Path.Combine(root, "exclusions.jsonl")));
        Assert.True(File.Exists(Path.Combine(root, "attribution.md")));
        Assert.Contains("conservative-default", await File.ReadAllTextAsync(Path.Combine(root, "licenses.json")));
    }
}
