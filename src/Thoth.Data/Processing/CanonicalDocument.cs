namespace Thoth.Data.Processing;

public sealed record CanonicalDocument(
    string Id,
    string SourceId,
    string Language,
    string ContentKind,
    string Text,
    string LicenseSpdx,
    string? Revision,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record TextNormalizationOptions(
    bool NormalizeToNfc = true,
    bool CollapseWhitespaceOutsideCode = true,
    int MaximumConsecutiveBlankLines = 2);
