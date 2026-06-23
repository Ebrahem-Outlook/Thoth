using System.Text.Json;
using Thoth.Core.Chat;

namespace Thoth.Core.Understanding;

public sealed class LlmUnderstandingService(
    IChatModel model,
    IUserUnderstandingService fallback) : IUserUnderstandingService
{
    public async Task<UnderstandingResult> UnderstandAsync(
        UnderstandingRequest request,
        CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
        Classify the user's message for an AI assistant. Return JSON only.
        Shape:
        {
          "intent": "general_chat | workspace_task | vision_chat | file_analysis | creative | research",
          "topic": "short topic",
          "language": "ar | en | mixed | other",
          "requiresTools": true,
          "requiresVision": false,
          "isLongContext": false,
          "confidence": 0.0,
          "summary": "one sentence"
        }

        Attachment content types: {{string.Join(", ", request.AttachmentContentTypes)}}
        User message:
        {{request.Text}}
        """;

        try
        {
            var response = await model.CompleteAsync(
                new ChatRequest(
                    [
                        new ChatMessage(ChatRole.System, "You are a strict JSON classifier."),
                        new ChatMessage(ChatRole.User, prompt)
                    ],
                    "thoth-understanding",
                    0),
                cancellationToken);

            var parsed = TryParse(response.Content);
            return parsed ?? await fallback.UnderstandAsync(request, cancellationToken);
        }
        catch
        {
            return await fallback.UnderstandAsync(request, cancellationToken);
        }
    }

    private static UnderstandingResult? TryParse(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(content[start..(end + 1)]);
            var root = document.RootElement;
            return new UnderstandingResult(
                GetString(root, "intent", "general_chat"),
                GetString(root, "topic", "general"),
                GetString(root, "language", "en"),
                GetBool(root, "requiresTools"),
                GetBool(root, "requiresVision"),
                GetBool(root, "isLongContext"),
                GetDouble(root, "confidence", 0.5),
                GetString(root, "summary", string.Empty));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetString(JsonElement root, string property, string fallback) =>
        root.TryGetProperty(property, out var element) ? element.GetString() ?? fallback : fallback;

    private static bool GetBool(JsonElement root, string property) =>
        root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.True;

    private static double GetDouble(JsonElement root, string property, double fallback) =>
        root.TryGetProperty(property, out var element) && element.TryGetDouble(out var value) ? value : fallback;
}
