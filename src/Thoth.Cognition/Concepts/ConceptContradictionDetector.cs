namespace Thoth.Cognition.Concepts;

public sealed record ConceptContradiction(
    bool IsContradiction,
    string ReasonCode);

public static class ConceptContradictionDetector
{
    private static readonly IReadOnlyDictionary<string, string> Opposites = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["safe"] = "dangerous",
        ["dangerous"] = "safe",
        ["supports"] = "does-not-support",
        ["does-not-support"] = "supports",
        ["allows"] = "forbids",
        ["forbids"] = "allows"
    };

    public static ConceptContradiction Compare(ContextualRelation left, ContextualRelation right)
    {
        if (!AreOpposite(left.RelationType, right.RelationType))
        {
            return new ConceptContradiction(false, "relation-types-compatible");
        }

        if (!HaveSameParticipants(left, right))
        {
            return new ConceptContradiction(false, "different-participants");
        }

        if (!left.Context.IsCompatibleWith(right.Context) || !right.Context.IsCompatibleWith(left.Context))
        {
            return new ConceptContradiction(false, "context-disjoint");
        }

        return new ConceptContradiction(true, "opposite-relation-overlapping-context");
    }

    private static bool AreOpposite(string left, string right) =>
        Opposites.TryGetValue(left, out var opposite) &&
        string.Equals(opposite, right, StringComparison.OrdinalIgnoreCase);

    private static bool HaveSameParticipants(ContextualRelation left, ContextualRelation right)
    {
        var leftSet = left.Participants.Select(participant => $"{participant.Role}:{participant.ConceptId.Value}").Order(StringComparer.Ordinal).ToArray();
        var rightSet = right.Participants.Select(participant => $"{participant.Role}:{participant.ConceptId.Value}").Order(StringComparer.Ordinal).ToArray();
        return leftSet.SequenceEqual(rightSet);
    }
}
