using System.Text.RegularExpressions;
using Thoth.Cognition.Concepts;
using Thoth.Cognition.Text;

namespace Thoth.Cognition.Tasks;

public sealed class CodeTaskExtractor
{
    public CodeGenerationTask? ExtractNewTask(Guid conversationId, string text, DateTimeOffset? now = null)
    {
        if (!LooksLikeCodeTask(text))
        {
            return null;
        }

        return BuildTask(conversationId, text, now ?? DateTimeOffset.UtcNow);
    }

    public CodeGenerationTask ExtractContinuation(Guid conversationId, string text, DateTimeOffset? now = null) =>
        BuildTask(conversationId, text, now ?? DateTimeOffset.UtcNow);

    public bool LooksLikeCodeTask(string text)
    {
        var normalized = ArabicTextNormalizer.NormalizeForMatching(text);
        return ProgrammingLanguageMatcher.Detect(text) != CognitiveProgrammingLanguage.Unknown ||
               ContainsAny(normalized, "code", "method", "meethod", "methd", "function", "class", "snippet") ||
               ArabicTextNormalizer.ContainsAny(text, "\u0643\u0648\u062f", "\u0645\u064a\u062b\u0648\u062f", "\u0645\u064a\u062b\u062f", "\u062f\u0627\u0644\u0647", "\u0643\u0644\u0627\u0633");
    }

    public bool IsRepositoryBound(string text)
    {
        var normalized = ArabicTextNormalizer.NormalizeForMatching(text);
        return Regex.IsMatch(text, @"[\w./\\-]+\.(cs|ts|html|scss|json|md|csproj|sln)", RegexOptions.IgnoreCase) ||
               ContainsAny(normalized, "repo", "repository", "workspace", "project", "src/", "tests/", "fix", "refactor", "run tests") ||
               ArabicTextNormalizer.ContainsAny(text, "\u0627\u0644\u0645\u0634\u0631\u0648\u0639", "\u0627\u0644\u0645\u0644\u0641", "\u0627\u0644\u0631\u064a\u0628\u0648");
    }

    private static CodeGenerationTask BuildTask(Guid conversationId, string text, DateTimeOffset now)
    {
        var language = ProgrammingLanguageMatcher.Detect(text);
        var artifact = DetectArtifact(text);
        var behavior = DetectBehavior(text);
        var task = new CodeGenerationTask(
            Guid.NewGuid(),
            conversationId,
            TaskStatus.Pending,
            language,
            artifact,
            behavior,
            [],
            null,
            [],
            [],
            now,
            now,
            0);

        task = ApplyBehaviorContract(task);
        return task with { Status = CodeTaskSlots.DetermineStatus(task) };
    }

    public static CodeGenerationTask ApplyBehaviorContract(CodeGenerationTask task)
    {
        if (!string.Equals(task.Behavior, CodeTaskBehaviors.Calculator, StringComparison.OrdinalIgnoreCase))
        {
            return task;
        }

        return task with
        {
            Inputs =
            [
                new CodeParameter("left", "number", "Left numeric operand."),
                new CodeParameter("right", "number", "Right numeric operand."),
                new CodeParameter("operation", "operation", "Arithmetic operation: add, subtract, multiply, or divide.")
            ],
            Output = "Calculated numeric result.",
            Validations =
            [
                new ValidationRequirement("division_by_zero", "Reject division by zero."),
                new ValidationRequirement("unsupported_operation", "Reject unsupported calculator operations.")
            ]
        };
    }

    private static CodeArtifactKind DetectArtifact(string text)
    {
        var normalized = ArabicTextNormalizer.NormalizeForMatching(text);
        if (ContainsAny(normalized, "method", "meethod", "methd") ||
            ArabicTextNormalizer.ContainsAny(text, "\u0645\u064a\u062b\u0648\u062f", "\u0645\u064a\u062b\u062f"))
        {
            return CodeArtifactKind.Method;
        }

        if (ContainsAny(normalized, "function") ||
            ArabicTextNormalizer.ContainsAny(text, "\u062f\u0627\u0644\u0647"))
        {
            return CodeArtifactKind.Function;
        }

        if (ContainsAny(normalized, "class") ||
            ArabicTextNormalizer.ContainsAny(text, "\u0643\u0644\u0627\u0633"))
        {
            return CodeArtifactKind.Class;
        }

        return CodeArtifactKind.Unknown;
    }

    private static string? DetectBehavior(string text) =>
        ArithmeticOperationMatcher.LooksLikeCalculator(text) ? CodeTaskBehaviors.Calculator : null;

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
