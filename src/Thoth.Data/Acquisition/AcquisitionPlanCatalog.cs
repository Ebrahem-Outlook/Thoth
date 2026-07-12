namespace Thoth.Data.Acquisition;

public static class AcquisitionPlanCatalog
{
    public static IReadOnlyList<AcquisitionSourceDefinition> Sources { get; } =
    [
        new(
            "arwiki",
            "Arabic Wikipedia articles dump",
            "wikimedia-dump",
            "https://dumps.wikimedia.org/arwiki/latest/arwiki-latest-pages-articles-multistream.xml.bz2",
            "CC-BY-SA-4.0",
            "https://foundation.wikimedia.org/wiki/Policy:Terms_of_Use",
            AttributionRequired: true,
            PilotCompressedByteCap: 2L * 1024 * 1024 * 1024,
            PilotExtractedByteCap: 400L * 1024 * 1024,
            RequiredApprovalFacts:
            [
                "official source URL",
                "license",
                "remote compressed size from HEAD or checksum metadata",
                "expected extracted bytes",
                "current free disk space"
            ],
            Steps:
            [
                Step(1, "probe", "HEAD official dump and checksum URL with a descriptive User-Agent.", true, false),
                Step(2, "approval", "Print source, license, size, expected disk use, and free disk before download.", false, true),
                Step(3, "download", "Resume-safe single-connection local download only after approval gates pass.", true, true),
                Step(4, "verify", "Verify official checksum before extraction.", false, false),
                Step(5, "extract", "Stream BZip2/XML, keep page/revision metadata, strip non-article boilerplate, cap pilot output.", false, false)
            ]),
        new(
            "simplewiki",
            "Simple English Wikipedia articles dump",
            "wikimedia-dump",
            "https://dumps.wikimedia.org/simplewiki/latest/simplewiki-latest-pages-articles-multistream.xml.bz2",
            "CC-BY-SA-4.0",
            "https://foundation.wikimedia.org/wiki/Policy:Terms_of_Use",
            AttributionRequired: true,
            PilotCompressedByteCap: 2L * 1024 * 1024 * 1024,
            PilotExtractedByteCap: 300L * 1024 * 1024,
            RequiredApprovalFacts:
            [
                "official source URL",
                "license",
                "remote compressed size from HEAD or checksum metadata",
                "expected extracted bytes",
                "current free disk space"
            ],
            Steps:
            [
                Step(1, "probe", "HEAD official dump and checksum URL with a descriptive User-Agent.", true, false),
                Step(2, "approval", "Print source, license, size, expected disk use, and free disk before download.", false, true),
                Step(3, "download", "Resume-safe single-connection local download only after approval gates pass.", true, true),
                Step(4, "verify", "Verify official checksum before extraction.", false, false),
                Step(5, "extract", "Stream BZip2/XML and cap normalized Simple English pilot text.", false, false)
            ]),
        new(
            "mdn-content",
            "MDN content repository",
            "git-repository",
            "https://github.com/mdn/content.git",
            "CC-BY-SA-2.5",
            "https://github.com/mdn/content/blob/main/LICENSE.md",
            AttributionRequired: true,
            PilotCompressedByteCap: null,
            PilotExtractedByteCap: 500L * 1024 * 1024,
            RequiredApprovalFacts:
            [
                "official repository URL",
                "resolved commit SHA",
                "license metadata",
                "planned path allowlist",
                "current free disk space"
            ],
            Steps:
            [
                Step(1, "approval", "Print shallow clone command, license, expected cap, and free disk before clone.", false, true),
                Step(2, "clone", "Shallow clone official repository locally.", true, true),
                Step(3, "inspect-license", "Read license from checked-out revision.", false, false),
                Step(4, "extract", "Extract only approved MDN paths, preserving prose/code labels and attribution.", false, false)
            ]),
        new(
            "oasst1",
            "OpenAssistant OASST1",
            "dataset-transport",
            "https://huggingface.co/datasets/OpenAssistant/oasst1",
            "Apache-2.0",
            "https://huggingface.co/datasets/OpenAssistant/oasst1",
            AttributionRequired: true,
            PilotCompressedByteCap: null,
            PilotExtractedByteCap: null,
            RequiredApprovalFacts:
            [
                "dataset identifier",
                "license",
                "pinned local transport package",
                "estimated local cache size",
                "current free disk space"
            ],
            Steps:
            [
                Step(1, "approval", "Print dataset ID, license, pinned package, size estimate, and free disk before local transport.", false, true),
                Step(2, "download", "Use pinned Hugging Face datasets only as local dataset transport, never inference.", true, true),
                Step(3, "filter", "Filter deleted/unsafe/low-quality records and languages ar/en.", false, false),
                Step(4, "split", "Split by message_tree_id to avoid leakage.", false, false),
                Step(5, "review-sample", "Produce a 100-record manual review sample.", false, false)
            ]),
        new(
            "curated-code",
            "Curated permissive code repositories",
            "git-repository-allowlist",
            "local-allowlist",
            "various-permissive",
            "data/manifests/licenses.json",
            AttributionRequired: true,
            PilotCompressedByteCap: null,
            PilotExtractedByteCap: 1536L * 1024 * 1024,
            RequiredApprovalFacts:
            [
                "repository URL",
                "resolved commit SHA",
                "checked-out license",
                "per-repository byte cap",
                "current free disk space"
            ],
            Steps:
            [
                Step(1, "review-allowlist", "Review repository URL and current license before clone.", true, true),
                Step(2, "clone", "Shallow clone only approved permissive repositories.", true, true),
                Step(3, "scan", "Exclude generated/vendor/binary/minified files and scan secrets.", false, false),
                Step(4, "split", "Split by repository so held-out repos never leak into train.", false, false)
            ]),
        new(
            "owned-synthetic",
            "Owned deterministic Thoth instruction data",
            "owned-synthetic",
            "local-generator",
            "Thoth-owned",
            "docs/training-data.md",
            AttributionRequired: false,
            PilotCompressedByteCap: null,
            PilotExtractedByteCap: null,
            RequiredApprovalFacts:
            [
                "local generator version",
                "deterministic seed",
                "example count",
                "verifier type"
            ],
            Steps:
            [
                Step(1, "generate", "Generate local verified calculator/procedure/tool-routing examples from deterministic templates.", false, false),
                Step(2, "verify", "Attach verifier metadata to every generated example.", false, false),
                Step(3, "split", "Split by template family to avoid continuation leakage.", false, false)
            ])
    ];

    public static AcquisitionSourceDefinition Resolve(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        return Sources.FirstOrDefault(source => source.SourceId.Equals(sourceId, StringComparison.OrdinalIgnoreCase))
               ?? throw new ArgumentOutOfRangeException(nameof(sourceId), $"Unknown acquisition source: {sourceId}");
    }

    private static AcquisitionPlanStep Step(
        int order,
        string name,
        string description,
        bool requiresNetwork,
        bool requiresExplicitApproval) =>
        new(order, name, description, requiresNetwork, requiresExplicitApproval);
}
