using Thoth.Cognition.Text;

namespace Thoth.Cognition.Concepts;

public sealed record ConceptActivationRequest(
    string Text,
    IReadOnlyList<ConceptId> ActiveConcepts,
    IReadOnlyDictionary<string, string> Context,
    int Limit = 16);

public sealed record ActivatedConcept(
    Concept Concept,
    double Score,
    string Reason);

public sealed record ActivatedRelation(
    ContextualRelation Relation,
    double Score,
    string Reason);

public sealed record ConceptActivationResult(
    IReadOnlyList<ActivatedConcept> Concepts,
    IReadOnlyList<ActivatedRelation> Relations);

public sealed class ConceptActivationService(IConceptGraphStore store)
{
    public async Task<ConceptActivationResult> ActivateAsync(
        ConceptActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        await store.EnsureCreatedAsync(cancellationToken);

        var normalizedTerms = ExtractTerms(request.Text)
            .Select(ArabicTextNormalizer.NormalizeForMatching)
            .Where(term => term.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToArray();

        var concepts = new Dictionary<ConceptId, ActivatedConcept>();
        foreach (var term in normalizedTerms)
        {
            var matches = await store.FindConceptsByAliasAsync(term, request.Limit, cancellationToken);
            foreach (var concept in matches)
            {
                var exactBoost = string.Equals(
                    ArabicTextNormalizer.NormalizeForMatching(concept.CanonicalName),
                    term,
                    StringComparison.OrdinalIgnoreCase)
                    ? 0.25
                    : 0;
                AddConcept(concepts, concept, concept.Confidence.Clamped + exactBoost, $"alias:{term}");
            }
        }

        if (request.ActiveConcepts.Count > 0)
        {
            var activeRelations = await store.GetRelationsForConceptsAsync(request.ActiveConcepts, request.Limit, cancellationToken);
            foreach (var relation in activeRelations)
            {
                foreach (var participant in relation.Participants)
                {
                    if (request.ActiveConcepts.Contains(participant.ConceptId))
                    {
                        continue;
                    }

                    var participantConcepts = await store.GetRelationsForConceptsAsync([participant.ConceptId], 1, cancellationToken);
                    if (participantConcepts.Count > 0)
                    {
                        continue;
                    }
                }
            }
        }

        var selectedConcepts = concepts.Values
            .OrderByDescending(concept => concept.Score)
            .ThenBy(concept => concept.Concept.CanonicalName, StringComparer.OrdinalIgnoreCase)
            .Take(request.Limit)
            .ToArray();

        var relationCandidates = await store.GetRelationsForConceptsAsync(
            selectedConcepts.Select(concept => concept.Concept.Id).Concat(request.ActiveConcepts).Distinct().ToArray(),
            request.Limit * 4,
            cancellationToken);

        var relations = relationCandidates
            .Where(relation => relation.Context.Values.All(context =>
                !request.Context.TryGetValue(context.Key, out var requested) ||
                string.Equals(requested, context.Value, StringComparison.OrdinalIgnoreCase)))
            .Select(relation => new ActivatedRelation(
                relation,
                relation.Confidence.Clamped + ContextCompatibilityBoost(relation.Context, request.Context),
                "one-hop-contextual"))
            .OrderByDescending(relation => relation.Score)
            .Take(request.Limit)
            .ToArray();

        return new ConceptActivationResult(selectedConcepts, relations);
    }

    private static void AddConcept(
        IDictionary<ConceptId, ActivatedConcept> concepts,
        Concept concept,
        double score,
        string reason)
    {
        if (concepts.TryGetValue(concept.Id, out var existing) && existing.Score >= score)
        {
            return;
        }

        concepts[concept.Id] = new ActivatedConcept(concept, Math.Clamp(score, 0, 1.5), reason);
    }

    private static double ContextCompatibilityBoost(RelationContext context, IReadOnlyDictionary<string, string> requestContext)
    {
        if (context.Values.Count == 0 || requestContext.Count == 0)
        {
            return 0;
        }

        var matches = context.Values.Count(pair =>
            requestContext.TryGetValue(pair.Key, out var value) &&
            string.Equals(value, pair.Value, StringComparison.OrdinalIgnoreCase));
        return Math.Min(matches * 0.05, 0.2);
    }

    private static IEnumerable<string> ExtractTerms(string text)
    {
        var normalized = ArabicTextNormalizer.NormalizeForMatching(text);
        foreach (var token in normalized.Split([' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries))
        {
            yield return token;
        }
    }
}
