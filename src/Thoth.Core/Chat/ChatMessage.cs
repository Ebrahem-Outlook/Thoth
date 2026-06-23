namespace Thoth.Core.Chat;

public sealed record ChatMessage(
    ChatRole Role,
    string Content,
    string? Name = null,
    IReadOnlyList<ChatAttachment>? Attachments = null);
