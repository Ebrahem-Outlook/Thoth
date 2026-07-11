namespace Thoth.Core.Understanding;

public sealed class HeuristicUnderstandingService : IUserUnderstandingService
{
    private static readonly string[] WorkspaceTerms =
    [
        "code",
        "file",
        "project",
        "repo",
        "workspace",
        "build",
        "test",
        "bug",
        "implement",
        "refactor",
        "api",
        "backend",
        "frontend",
        "angular",
        "dotnet",
        "read",
        "write",
        "search",
        "run",
        "model",
        "llm",
        "reason",
        "reasoning",
        "brain",
        "neural",
        "train",
        "think",
        "intelligence",
        "\u0646\u0641\u0630",
        "\u0627\u0628\u0646\u064a",
        "\u0627\u0643\u062a\u0628",
        "\u0639\u062f\u0644",
        "\u062d\u0633\u0646",
        "\u0645\u0634\u0631\u0648\u0639",
        "\u0645\u0644\u0641",
        "\u0643\u0648\u062f",
        "\u0628\u0627\u0643",
        "\u0641\u0631\u0648\u0646\u062a",
        "\u0648\u0627\u062c\u0647\u0629",
        "\u0627\u0646\u062c\u0644\u0648\u0631",
        "\u0645\u0648\u062f\u064a\u0644",
        "\u064a\u0641\u0643\u0631",
        "\u0639\u0642\u0644",
        "\u0630\u0643\u064a",
        "\u062a\u062f\u0631\u064a\u0628",
        "\u0639\u0635\u0628\u064a"
    ];

    private static readonly string[] FrontendTerms =
    [
        "angular",
        "frontend",
        "\u0641\u0631\u0648\u0646\u062a",
        "\u0648\u0627\u062c\u0647\u0629",
        "\u0627\u0646\u062c\u0644\u0648\u0631"
    ];

    private static readonly string[] BackendTerms =
    [
        "api",
        "backend",
        "\u0628\u0627\u0643"
    ];

    private static readonly string[] ModelTerms =
    [
        "model",
        "llm",
        "reasoning",
        "brain",
        "neural",
        "train",
        "think",
        "intelligence",
        "\u0645\u0648\u062f\u064a\u0644",
        "\u064a\u0641\u0643\u0631",
        "\u0639\u0642\u0644",
        "\u0630\u0643\u064a",
        "\u062a\u062f\u0631\u064a\u0628",
        "\u0639\u0635\u0628\u064a"
    ];

    public Task<UnderstandingResult> UnderstandAsync(
        UnderstandingRequest request,
        CancellationToken cancellationToken = default)
    {
        var text = request.Text.Trim();
        var lower = text.ToLowerInvariant();
        var requiresVision = request.AttachmentContentTypes.Any(type => type.StartsWith("image/", StringComparison.OrdinalIgnoreCase));
        if (IsCasualChat(lower, text) && !requiresVision)
        {
            return Task.FromResult(new UnderstandingResult(
                "general_chat",
                "general",
                UnderstandingResult.DetectLanguage(text),
                false,
                false,
                text.Length > 8000,
                0.92,
                text.Length > 600 ? text[..600] + "..." : text));
        }

        var requiresTools = LooksLikeWorkspaceTask(lower) || LooksLikeFileTask(lower) || LooksLikeCommandTask(lower);
        var intent = requiresTools ? "workspace_task" : requiresVision ? "vision_chat" : "general_chat";
        var topic = InferTopic(lower, request.AttachmentContentTypes);
        var confidence = requiresTools || requiresVision ? 0.78 : 0.6;

        return Task.FromResult(new UnderstandingResult(
            intent,
            topic,
            UnderstandingResult.DetectLanguage(text),
            requiresTools,
            requiresVision,
            text.Length > 8000,
            confidence,
            text.Length > 600 ? text[..600] + "..." : text));
    }

    private static bool LooksLikeWorkspaceTask(string text) =>
        ContainsAny(text, WorkspaceTerms);

    private static bool LooksLikeFileTask(string text) =>
        text.Contains(".cs", StringComparison.OrdinalIgnoreCase) ||
        text.Contains(".ts", StringComparison.OrdinalIgnoreCase) ||
        text.Contains(".html", StringComparison.OrdinalIgnoreCase) ||
        text.Contains(".scss", StringComparison.OrdinalIgnoreCase) ||
        text.Contains(".json", StringComparison.OrdinalIgnoreCase) ||
        text.Contains(".md", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeCommandTask(string text) =>
        text.StartsWith("run ", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("dotnet ", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("npm ", StringComparison.OrdinalIgnoreCase);

    private static string InferTopic(string text, IReadOnlyList<string> contentTypes)
    {
        if (contentTypes.Any(type => type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)))
        {
            return "image";
        }

        if (ContainsAny(text, FrontendTerms))
        {
            return "frontend";
        }

        if (ContainsAny(text, ModelTerms))
        {
            return "model";
        }

        if (ContainsAny(text, BackendTerms))
        {
            return "backend";
        }

        if (ContainsAny(text, "code", "bug", "\u0643\u0648\u062f"))
        {
            return "coding";
        }

        return "general";
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool IsCasualChat(string lower, string text)
    {
        var normalized = lower.Trim().Trim('.', '!', '?');
        return normalized is "hi" or "hello" or "hey" or "yo" or "sup" or "thanks" or "thank you" ||
               ContainsAny(lower, "do you understand me", "understand me", "are you following", "got me") ||
               ContainsAny(text, "\u0627\u0647\u0644\u0627", "\u0623\u0647\u0644\u0627", "\u0645\u0631\u062d\u0628\u0627", "\u0633\u0644\u0627\u0645", "\u0627\u0632\u064a\u0643", "\u0634\u0643\u0631\u0627", "\u0627\u0646\u062a \u0641\u0627\u0647\u0645\u0646\u064a", "\u0623\u0646\u062a \u0641\u0627\u0647\u0645\u0646\u064a", "\u0641\u0627\u0647\u0645\u0646\u064a", "\u0641\u0647\u0645\u062a\u0646\u064a");
    }
}
