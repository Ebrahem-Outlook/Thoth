using Thoth.Cognition.Text;

namespace Thoth.Cognition.Concepts;

public readonly record struct ConceptId(string Value)
{
    public static ConceptId NewId(string prefix = "concept") => new($"{prefix}:{Guid.NewGuid():N}");

    public override string ToString() => Value;
}

public enum ConceptStatus
{
    Proposed,
    Verified,
    Disputed,
    Deprecated,
    Removed
}

public sealed record ConfidenceScore(double Value)
{
    public double Clamped => Math.Clamp(Value, 0, 1);
}

public sealed record EvidenceSource(
    string SourceId,
    string SourceType,
    string Uri,
    string License,
    string Attribution,
    DateTimeOffset CreatedAt);

public sealed record Concept(
    ConceptId Id,
    string ConceptType,
    string CanonicalName,
    ConceptStatus Status,
    ConfidenceScore Confidence,
    string SourceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record ConceptAlias(
    string AliasId,
    ConceptId ConceptId,
    string Alias,
    string NormalizedAlias,
    string? Language,
    ConfidenceScore Confidence,
    string SourceId,
    DateTimeOffset CreatedAt)
{
    public static ConceptAlias Create(
        ConceptId conceptId,
        string alias,
        string? language,
        double confidence,
        string sourceId,
        DateTimeOffset? createdAt = null) =>
        new(
            $"alias:{Guid.NewGuid():N}",
            conceptId,
            alias,
            ArabicTextNormalizer.NormalizeForMatching(alias),
            language,
            new ConfidenceScore(confidence),
            sourceId,
            createdAt ?? DateTimeOffset.UtcNow);
}

public enum RelationStatus
{
    Proposed,
    Verified,
    Disputed,
    Deprecated,
    Removed
}

public sealed record RelationParticipant(
    string Role,
    ConceptId ConceptId);

public sealed record RelationContext(
    IReadOnlyDictionary<string, string> Values)
{
    public bool IsCompatibleWith(RelationContext other)
    {
        foreach (var (key, value) in Values)
        {
            if (other.Values.TryGetValue(key, out var otherValue) &&
                !string.Equals(value, otherValue, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}

public sealed record ContextualRelation(
    string RelationId,
    string RelationType,
    IReadOnlyList<RelationParticipant> Participants,
    RelationContext Context,
    IReadOnlyList<string> Conditions,
    RelationStatus Status,
    ConfidenceScore Confidence,
    string SourceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static ContextualRelation Create(
        string relationType,
        IReadOnlyList<RelationParticipant> participants,
        RelationContext context,
        IReadOnlyList<string> conditions,
        RelationStatus status,
        double confidence,
        string sourceId,
        IReadOnlyDictionary<string, string>? metadata = null,
        DateTimeOffset? createdAt = null)
    {
        var now = createdAt ?? DateTimeOffset.UtcNow;
        return new ContextualRelation(
            $"relation:{Guid.NewGuid():N}",
            relationType,
            participants,
            context,
            conditions,
            status,
            new ConfidenceScore(confidence),
            sourceId,
            now,
            now,
            metadata ?? new Dictionary<string, string>());
    }
}
