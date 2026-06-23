namespace Thoth.Core.Conversations;

public sealed record ChatTurnRequest(
    Guid? ConversationId,
    string Content,
    IReadOnlyList<Guid> AttachmentIds,
    string WorkingDirectory,
    string Model,
    bool UseTools = true,
    int MaxSteps = 8);
