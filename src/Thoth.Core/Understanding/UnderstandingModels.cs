namespace Thoth.Core.Understanding;

public sealed record UnderstandingRequest(
    string Text,
    IReadOnlyList<string> AttachmentContentTypes,
    string? Project = null,
    string? ActiveTaskSummary = null);

public sealed record UnderstandingResult(
    string Intent,
    string Topic,
    string Language,
    bool RequiresTools,
    bool RequiresVision,
    bool IsLongContext,
    double Confidence,
    string Summary)
{
    public static UnderstandingResult General(string text) =>
        new("general_chat", "general", DetectLanguage(text), false, false, text.Length > 8000, 0.55, text);

    internal static string DetectLanguage(string text)
    {
        return text.Any(c => c >= 0x0600 && c <= 0x06FF) ? "ar" : "en";
    }
}
