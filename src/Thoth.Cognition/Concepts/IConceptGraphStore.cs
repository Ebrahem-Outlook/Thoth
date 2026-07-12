namespace Thoth.Cognition.Concepts;

public interface IConceptGraphStore
{
    Task EnsureCreatedAsync(CancellationToken cancellationToken = default);

    Task SaveSourceAsync(EvidenceSource source, CancellationToken cancellationToken = default);

    Task UpsertConceptAsync(Concept concept, CancellationToken cancellationToken = default);

    Task AddAliasAsync(ConceptAlias alias, CancellationToken cancellationToken = default);

    Task AddRelationAsync(ContextualRelation relation, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Concept>> FindConceptsByAliasAsync(
        string normalizedAlias,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContextualRelation>> GetRelationsForConceptsAsync(
        IReadOnlyList<ConceptId> conceptIds,
        int limit,
        CancellationToken cancellationToken = default);
}

public sealed class InMemoryConceptGraphStore : IConceptGraphStore
{
    private readonly Dictionary<string, EvidenceSource> sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ConceptId, Concept> concepts = new();
    private readonly List<ConceptAlias> aliases = [];
    private readonly List<ContextualRelation> relations = [];

    public Task EnsureCreatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SaveSourceAsync(EvidenceSource source, CancellationToken cancellationToken = default)
    {
        sources[source.SourceId] = source;
        return Task.CompletedTask;
    }

    public Task UpsertConceptAsync(Concept concept, CancellationToken cancellationToken = default)
    {
        concepts[concept.Id] = concept;
        return Task.CompletedTask;
    }

    public Task AddAliasAsync(ConceptAlias alias, CancellationToken cancellationToken = default)
    {
        aliases.RemoveAll(existing =>
            existing.ConceptId == alias.ConceptId &&
            string.Equals(existing.NormalizedAlias, alias.NormalizedAlias, StringComparison.OrdinalIgnoreCase));
        aliases.Add(alias);
        return Task.CompletedTask;
    }

    public Task AddRelationAsync(ContextualRelation relation, CancellationToken cancellationToken = default)
    {
        relations.RemoveAll(existing => string.Equals(existing.RelationId, relation.RelationId, StringComparison.OrdinalIgnoreCase));
        relations.Add(relation);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Concept>> FindConceptsByAliasAsync(string normalizedAlias, int limit, CancellationToken cancellationToken = default)
    {
        var matches = aliases
            .Where(alias => alias.NormalizedAlias.Contains(normalizedAlias, StringComparison.OrdinalIgnoreCase) ||
                            normalizedAlias.Contains(alias.NormalizedAlias, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(alias => alias.Confidence.Clamped)
            .Select(alias => concepts.GetValueOrDefault(alias.ConceptId))
            .Where(concept => concept is not null && concept.Status != ConceptStatus.Removed)
            .Distinct()
            .Take(Math.Clamp(limit, 1, 100))
            .Cast<Concept>()
            .ToArray();

        return Task.FromResult((IReadOnlyList<Concept>)matches);
    }

    public Task<IReadOnlyList<ContextualRelation>> GetRelationsForConceptsAsync(IReadOnlyList<ConceptId> conceptIds, int limit, CancellationToken cancellationToken = default)
    {
        var set = conceptIds.ToHashSet();
        var matches = relations
            .Where(relation => relation.Status != RelationStatus.Removed &&
                               relation.Participants.Any(participant => set.Contains(participant.ConceptId)))
            .OrderByDescending(relation => relation.Confidence.Clamped)
            .Take(Math.Clamp(limit, 1, 200))
            .ToArray();

        return Task.FromResult((IReadOnlyList<ContextualRelation>)matches);
    }
}
