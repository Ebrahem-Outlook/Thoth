namespace Thoth.Api.Contracts;

public sealed record CreateConversationRequest(string? Title, string? Project);

public sealed record UpdateConversationRequest(
    string? Title,
    bool? IsPinned,
    bool? IsArchived,
    string? Project);

public sealed record StreamChatRequest(
    string Content,
    IReadOnlyList<Guid>? AttachmentIds,
    string? Model,
    bool? UseTools,
    int? MaxSteps);

public sealed record ChatResponseDto(
    Guid ConversationId,
    Guid UserMessageId,
    Guid AssistantMessageId,
    string AssistantContent,
    object Understanding,
    object? AgentRun);
