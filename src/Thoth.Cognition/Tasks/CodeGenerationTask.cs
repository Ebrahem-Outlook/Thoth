using Thoth.Cognition.Concepts;

namespace Thoth.Cognition.Tasks;

public enum TaskStatus
{
    Pending,
    AwaitingDetails,
    Ready,
    Completed,
    Abandoned
}

public sealed record TaskTurn(
    Guid MessageId,
    string Text,
    DateTimeOffset CreatedAt);

public sealed record CodeParameter(
    string Name,
    string Type,
    string Description);

public sealed record ValidationRequirement(
    string Rule,
    string Message);

public sealed record CodeGenerationTask(
    Guid Id,
    Guid ConversationId,
    TaskStatus Status,
    CognitiveProgrammingLanguage Language,
    CodeArtifactKind ArtifactKind,
    string? Behavior,
    IReadOnlyList<CodeParameter> Inputs,
    string? Output,
    IReadOnlyList<ValidationRequirement> Validations,
    IReadOnlyList<TaskTurn> Turns,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version)
{
    public IReadOnlyList<string> MissingSlots => CodeTaskSlots.GetMissingSlots(this);

    public bool IsReady => MissingSlots.Count == 0 && Status is not TaskStatus.Completed and not TaskStatus.Abandoned;
}

public static class CodeTaskSlots
{
    public static IReadOnlyList<string> GetMissingSlots(CodeGenerationTask task)
    {
        var missing = new List<string>();
        if (task.Language == CognitiveProgrammingLanguage.Unknown)
        {
            missing.Add("language");
        }

        if (task.ArtifactKind == CodeArtifactKind.Unknown)
        {
            missing.Add("artifact kind");
        }

        if (string.IsNullOrWhiteSpace(task.Behavior))
        {
            missing.Add("behavior");
        }

        if (task.Inputs.Count == 0 && string.Equals(task.Behavior, CodeTaskBehaviors.Calculator, StringComparison.OrdinalIgnoreCase))
        {
            missing.Add("inputs");
        }

        if (string.IsNullOrWhiteSpace(task.Output) &&
            string.Equals(task.Behavior, CodeTaskBehaviors.Calculator, StringComparison.OrdinalIgnoreCase))
        {
            missing.Add("output");
        }

        return missing;
    }

    public static TaskStatus DetermineStatus(CodeGenerationTask task)
    {
        if (task.Status is TaskStatus.Completed or TaskStatus.Abandoned)
        {
            return task.Status;
        }

        return GetMissingSlots(task).Count == 0 ? TaskStatus.Ready : TaskStatus.AwaitingDetails;
    }
}

public static class CodeTaskBehaviors
{
    public const string Calculator = "calculator";
}
