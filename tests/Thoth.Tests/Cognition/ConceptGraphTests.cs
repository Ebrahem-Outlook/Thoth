using Thoth.Cognition.Concepts;
using Thoth.Memory.Cognition;

namespace Thoth.Tests.Cognition;

public sealed class ConceptGraphTests
{
    [Fact]
    public async Task Activation_ReturnsSeededCalculatorRelationsFromSqlite()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new SqliteConceptGraphStore(databasePath);
            await BuiltinConceptGraphSeeder.SeedAsync(store);
            var activation = new ConceptActivationService(store);

            var result = await activation.ActivateAsync(new ConceptActivationRequest(
                "build calculator divide",
                [],
                new Dictionary<string, string> { ["task"] = "calculator" }));

            Assert.Contains(result.Concepts, concept => concept.Concept.Id.Value == "concept:calculator-procedure");
            Assert.Contains(result.Concepts, concept => concept.Concept.Id.Value == "concept:operation:divide");
            Assert.Contains(result.Relations, relation =>
                relation.Relation.RelationType == "supports-operation" &&
                relation.Relation.Conditions.Contains("right operand must not be zero"));
        }
        finally
        {
            TryDelete(databasePath);
        }
    }

    [Fact]
    public void ContradictionDetector_RespectsRelationContext()
    {
        var fire = new ConceptId("concept:fire");
        var cookingSafe = BuildFireRelation("safe", fire, new Dictionary<string, string>
        {
            ["purpose"] = "cooking",
            ["controlled"] = "true"
        });
        var buildingDangerous = BuildFireRelation("dangerous", fire, new Dictionary<string, string>
        {
            ["location"] = "building",
            ["controlled"] = "false"
        });
        var kitchenDangerous = BuildFireRelation("dangerous", fire, new Dictionary<string, string>
        {
            ["purpose"] = "cooking",
            ["controlled"] = "true"
        });

        Assert.False(ConceptContradictionDetector.Compare(cookingSafe, buildingDangerous).IsContradiction);
        Assert.True(ConceptContradictionDetector.Compare(cookingSafe, kitchenDangerous).IsContradiction);
    }

    private static ContextualRelation BuildFireRelation(
        string relationType,
        ConceptId fire,
        IReadOnlyDictionary<string, string> context) =>
        ContextualRelation.Create(
            relationType,
            [new RelationParticipant("subject", fire)],
            new RelationContext(context),
            [],
            RelationStatus.Verified,
            0.95,
            BuiltinConceptGraphSeeder.BuiltInSourceId);

    private static string CreateTempDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"thoth-concept-graph-{Guid.NewGuid():N}.sqlite");

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
