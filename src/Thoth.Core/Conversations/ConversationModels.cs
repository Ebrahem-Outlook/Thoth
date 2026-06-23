using Thoth.Core.Chat;

namespace Thoth.Core.Conversations;

public sealed record Conversation(
    Guid Id,
    string Title,
    string? Project,
    bool IsPinned,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int MessageCount);

public sealed record ConversationMessage(
    Guid Id,
    Guid ConversationId,
    ChatRole Role,
    string Content,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ConversationAttachment> Attachments,
    string? Intent = null,
    string? MetadataJson = null);

public sealed record ConversationAttachment(
    Guid Id,
    Guid? ConversationId,
    Guid? MessageId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string StoragePath,
    DateTimeOffset CreatedAt);

public sealed record ConversationDetail(
    Conversation Conversation,
    IReadOnlyList<ConversationMessage> Messages);
