namespace Thoth.Cognition.Concepts;

public static class BuiltinConceptGraphSeeder
{
    public const string BuiltInSourceId = "source:thoth-builtins-v1";

    public static async Task SeedAsync(IConceptGraphStore store, CancellationToken cancellationToken = default)
    {
        await store.EnsureCreatedAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var source = new EvidenceSource(
            BuiltInSourceId,
            "built-in",
            "thoth://builtins/cognitive-core",
            "Thoth internal",
            "Thoth built-in deterministic concepts",
            now);
        await store.SaveSourceAsync(source, cancellationToken);

        var calculator = new Concept(
            new ConceptId("concept:calculator-procedure"),
            "procedure",
            "calculator procedure",
            ConceptStatus.Verified,
            new ConfidenceScore(1),
            BuiltInSourceId,
            now,
            now,
            new Dictionary<string, string> { ["procedureId"] = "calculator.method.v1" });

        await store.UpsertConceptAsync(calculator, cancellationToken);
        foreach (var alias in new[] { "calculator", "calc", "\u062d\u0627\u0633\u0628\u0647", "\u0627\u0644\u0647 \u062d\u0627\u0633\u0628\u0647" })
        {
            await store.AddAliasAsync(ConceptAlias.Create(calculator.Id, alias, null, 1, BuiltInSourceId, now), cancellationToken);
        }

        foreach (var operation in new[] { "add", "subtract", "multiply", "divide" })
        {
            var concept = new Concept(
                new ConceptId($"concept:operation:{operation}"),
                "arithmetic-operation",
                operation,
                ConceptStatus.Verified,
                new ConfidenceScore(1),
                BuiltInSourceId,
                now,
                now,
                new Dictionary<string, string>());
            await store.UpsertConceptAsync(concept, cancellationToken);
            await store.AddAliasAsync(ConceptAlias.Create(concept.Id, operation, "en", 1, BuiltInSourceId, now), cancellationToken);

            var relation = ContextualRelation.Create(
                "supports-operation",
                [
                    new RelationParticipant("procedure", calculator.Id),
                    new RelationParticipant("operation", concept.Id)
                ],
                new RelationContext(new Dictionary<string, string> { ["task"] = "calculator" }),
                operation == "divide" ? ["right operand must not be zero"] : [],
                RelationStatus.Verified,
                1,
                BuiltInSourceId,
                createdAt: now);
            await store.AddRelationAsync(relation, cancellationToken);
        }
    }
}
