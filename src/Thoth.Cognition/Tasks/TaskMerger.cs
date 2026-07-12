using Thoth.Cognition.Concepts;

namespace Thoth.Cognition.Tasks;

public sealed class TaskMerger(CodeTaskExtractor? extractor = null)
{
    private readonly CodeTaskExtractor extractor = extractor ?? new CodeTaskExtractor();

    public CodeGenerationTask Merge(CodeGenerationTask activeTask, string text, DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        var incoming = extractor.ExtractContinuation(activeTask.ConversationId, text, timestamp);
        var merged = activeTask with
        {
            Language = activeTask.Language != CognitiveProgrammingLanguage.Unknown ? activeTask.Language : incoming.Language,
            ArtifactKind = activeTask.ArtifactKind != CodeArtifactKind.Unknown ? activeTask.ArtifactKind : incoming.ArtifactKind,
            Behavior = string.IsNullOrWhiteSpace(activeTask.Behavior) ? incoming.Behavior : activeTask.Behavior,
            UpdatedAt = timestamp,
            Version = activeTask.Version + 1
        };

        merged = CodeTaskExtractor.ApplyBehaviorContract(merged);
        return merged with { Status = CodeTaskSlots.DetermineStatus(merged) };
    }

    public CodeGenerationTask AddTurn(CodeGenerationTask task, Guid messageId, string text, DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        return task with
        {
            Turns = [.. task.Turns, new TaskTurn(messageId, text, timestamp)],
            UpdatedAt = timestamp,
            Version = task.Version + 1
        };
    }
}
