using Thoth.Core.Agent;
using Thoth.Core.Chat;
using Thoth.Core.Understanding;

namespace Thoth.Core.Conversations;

public sealed record ChatTurnResult(
    ConversationDetail Conversation,
    ConversationMessage UserMessage,
    ConversationMessage AssistantMessage,
    UnderstandingResult Understanding,
    AgentRun? AgentRun,
    AssistantResponseKind AssistantKind = AssistantResponseKind.DirectAnswer,
    IReadOnlyList<string>? SuggestedDetails = null,
    string? ActiveTaskSummary = null);
