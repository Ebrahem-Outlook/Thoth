namespace Thoth.Core.Understanding;

public sealed class HeuristicUnderstandingService : IUserUnderstandingService
{
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

    private static bool LooksLikeWorkspaceTask(string text)
    {
        string[] terms =
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
            "نفذ",
            "ابني",
            "اكتب",
            "عدل",
            "مشروع",
            "ملف",
            "باك",
            "فرونت"
        ];

        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

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

        if (text.Contains("angular", StringComparison.OrdinalIgnoreCase) || text.Contains("frontend", StringComparison.OrdinalIgnoreCase))
        {
            return "frontend";
        }

        if (text.Contains("api", StringComparison.OrdinalIgnoreCase) || text.Contains("backend", StringComparison.OrdinalIgnoreCase))
        {
            return "backend";
        }

        if (text.Contains("code", StringComparison.OrdinalIgnoreCase) || text.Contains("bug", StringComparison.OrdinalIgnoreCase))
        {
            return "coding";
        }

        return "general";
    }
}
