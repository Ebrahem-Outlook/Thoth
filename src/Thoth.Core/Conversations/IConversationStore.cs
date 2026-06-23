using Thoth.Core.Chat;

namespace Thoth.Core.Conversations;

public interface IConversationStore
{
    Task EnsureCreatedAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Conversation>> ListAsync(
        string? query = null,
        string? project = null,
        bool includeArchived = false,
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<Conversation> CreateAsync(
        string title,
        string? project = null,
        CancellationToken cancellationToken = default);

    Task<ConversationDetail?> GetAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    Task<Conversation?> UpdateAsync(
        Guid conversationId,
        string? title = null,
        bool? isPinned = null,
        bool? isArchived = null,
        string? project = null,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid conversationId, CancellationToken cancellationToken = default);

    Task<ConversationMessage> AddMessageAsync(
        Guid conversationId,
        ChatRole role,
        string content,
        IReadOnlyList<Guid>? attachmentIds = null,
        string? intent = null,
        string? metadataJson = null,
        CancellationToken cancellationToken = default);

    Task<ConversationAttachment> AddAttachmentAsync(
        string fileName,
        string contentType,
        long sizeBytes,
        string storagePath,
        Guid? conversationId = null,
        Guid? messageId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConversationAttachment>> GetAttachmentsAsync(
        IReadOnlyList<Guid> attachmentIds,
        CancellationToken cancellationToken = default);

    Task<ConversationAttachment?> GetAttachmentAsync(
        Guid attachmentId,
        CancellationToken cancellationToken = default);
}
