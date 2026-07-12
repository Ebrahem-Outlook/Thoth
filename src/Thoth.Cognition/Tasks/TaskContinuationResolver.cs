using Thoth.Cognition.Concepts;
using Thoth.Cognition.Text;

namespace Thoth.Cognition.Tasks;

public sealed class TaskContinuationResolver(CodeTaskExtractor? extractor = null)
{
    private readonly CodeTaskExtractor extractor = extractor ?? new CodeTaskExtractor();

    public bool IsContinuation(CodeGenerationTask activeTask, string text)
    {
        if (activeTask.Status is TaskStatus.Completed or TaskStatus.Abandoned)
        {
            return false;
        }

        if (extractor.IsRepositoryBound(text))
        {
            return false;
        }

        var continuation = extractor.ExtractContinuation(activeTask.ConversationId, text);
        var hasNewSlot =
            continuation.Language != CognitiveProgrammingLanguage.Unknown ||
            continuation.ArtifactKind != CodeArtifactKind.Unknown ||
            !string.IsNullOrWhiteSpace(continuation.Behavior);

        if (hasNewSlot)
        {
            return true;
        }

        var normalized = ArabicTextNormalizer.NormalizeForMatching(text);
        return ContainsAny(normalized, "it should", "should", "work as", "make it", "it works as") ||
               ArabicTextNormalizer.ContainsAny(text, "\u062a\u0634\u062a\u063a\u0644", "\u062a\u0639\u0645\u0644", "\u0639\u0628\u0627\u0631\u0647 \u0639\u0646", "\u0648\u0638\u064a\u0641\u062a\u0647\u0627");
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
