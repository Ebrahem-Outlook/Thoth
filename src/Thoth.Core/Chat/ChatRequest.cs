namespace Thoth.Core.Chat;

public sealed record ChatRequest(
    IReadOnlyList<ChatMessage> Messages,
    string Model,
    double Temperature = 0.2,
    IReadOnlyDictionary<string, string>? Metadata = null);
