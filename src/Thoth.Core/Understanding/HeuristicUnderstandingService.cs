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
        "\u0627\u0646\u062c\u0644\u0648\u0631"
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

    public Task<UnderstandingResult> UnderstandAsync(
        UnderstandingRequest request,
        CancellationToken cancellationToken = default)
    {
        var text = request.Text.Trim();
        var lower = text.ToLowerInvariant();
        var requiresVision = request.AttachmentContentTypes.Any(type => type.StartsWith("image/", StringComparison.OrdinalIgnoreCase));
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
}
