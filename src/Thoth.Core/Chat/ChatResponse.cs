namespace Thoth.Core.Chat;

public sealed record ChatResponse(
    string Content,
    string Model,
    int? PromptTokens = null,
    int? CompletionTokens = null);
