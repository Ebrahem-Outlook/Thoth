namespace Thoth.Data.Manifests;

public sealed record DataSourceManifest(
    int SchemaVersion,
    IReadOnlyList<DataSourceRecord> Sources);

public sealed record DataSourceRecord(
    string SourceId,
    string SourceType,
    string SourceUrl,
    string? Revision,
    string LicenseSpdx,
    string? LicenseUrl,
    bool AttributionRequired,
    string Language,
    string ContentKind,
    string TrustTier);

public sealed record DataDocumentRecord(
    string DocumentId,
    string SourceId,
    string SourceType,
    string SourceUrl,
    string? Revision,
    DateTimeOffset RetrievedUtc,
    string LicenseSpdx,
    string? LicenseUrl,
    bool AttributionRequired,
    string Language,
    string ContentKind,
    string RelativePath,
    long Bytes,
    string Sha256,
    bool ContainsSecrets,
    bool ContainsPii,
    string Split);

public sealed record DatasetBuildManifest(
    int SchemaVersion,
    string BuildName,
    int DeterministicSeed,
    string Policy,
    IReadOnlyList<string> RequiredManifests,
    IReadOnlyDictionary<string, string> Notes);

public sealed record DataExclusionRecord(
    string Id,
    string SourceId,
    string RelativePath,
    string Reason,
    DateTimeOffset RecordedUtc);
