using System.Text;
using System.Text.Json;
using Thoth.Core.Agent;
using Thoth.Core.Chat;
using Thoth.Core.Understanding;

namespace Thoth.Core.Conversations;

public sealed class ChatOrchestrator(
    IConversationStore conversations,
    IUserUnderstandingService understanding,
    AgentEngine agentEngine,
    IChatModel chatModel)
{
    public async Task<ChatTurnResult> SendAsync(
        ChatTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        await conversations.EnsureCreatedAsync(cancellationToken);

        var conversation = request.ConversationId is Guid existingId
            ? (await conversations.GetAsync(existingId, cancellationToken))?.Conversation
            : null;

        conversation ??= await conversations.CreateAsync(CreateTitle(request.Content), cancellationToken: cancellationToken);

        var attachments = request.AttachmentIds.Count == 0
            ? []
            : await conversations.GetAttachmentsAsync(request.AttachmentIds, cancellationToken);

        var understood = await understanding.UnderstandAsync(
            new UnderstandingRequest(
                request.Content,
                attachments.Select(attachment => attachment.ContentType).ToArray(),
                conversation.Project),
            cancellationToken);

        var userMessage = await conversations.AddMessageAsync(
            conversation.Id,
            ChatRole.User,
            request.Content,
            request.AttachmentIds,
            understood.Intent,
            JsonSerializer.Serialize(understood),
            cancellationToken);

        AgentRun? agentRun = null;
        string assistantText;

        if (request.UseTools && understood.RequiresTools)
        {
            var goal = BuildAgentGoal(request.Content, attachments);
            agentRun = await agentEngine.RunAsync(
                new AgentRequest(goal, request.WorkingDirectory, request.Model, request.MaxSteps),
                cancellationToken);
            assistantText = agentRun.FinalAnswer;
        }
        else
        {
            assistantText = await ReplyDirectlyAsync(request, conversation.Id, attachments, understood, cancellationToken);
        }

        var assistantMessage = await conversations.AddMessageAsync(
            conversation.Id,
            ChatRole.Assistant,
            assistantText,
            intent: "assistant_response",
            metadataJson: JsonSerializer.Serialize(new
            {
                understood.Intent,
                understood.Topic,
                understood.RequiresTools,
                agentRunId = agentRun?.RunId
            }),
            cancellationToken: cancellationToken);

        var detail = await conversations.GetAsync(conversation.Id, cancellationToken)
                     ?? new ConversationDetail(conversation, [userMessage, assistantMessage]);

        return new ChatTurnResult(detail, userMessage, assistantMessage, understood, agentRun);
    }

    private async Task<string> ReplyDirectlyAsync(
        ChatTurnRequest request,
        Guid conversationId,
        IReadOnlyList<ConversationAttachment> attachments,
        UnderstandingResult understood,
        CancellationToken cancellationToken)
    {
        var detail = await conversations.GetAsync(conversationId, cancellationToken);
        var history = detail?.Messages.TakeLast(20).Select(message =>
            new ChatMessage(
                message.Role,
                message.Content,
                Attachments: message.Attachments.Select(ToChatAttachment).ToArray()))
            .ToList() ?? [];

        history.Add(new ChatMessage(ChatRole.User, request.Content, Attachments: attachments.Select(ToChatAttachment).ToArray()));

        var system = new ChatMessage(ChatRole.System, $$"""
        You are Thoth, a capable Arabic/English AI agent.
        Understand the user's exact intent before answering.
        Be direct, useful, and honest about what you can and cannot inspect.
        Current intent: {{understood.Intent}}
        Topic: {{understood.Topic}}
        Language: {{understood.Language}}
        """);

        var response = await chatModel.CompleteAsync(
            new ChatRequest([system, .. history], request.Model, 0.25),
            cancellationToken);

        return response.Content.Trim();
    }

    private static string BuildAgentGoal(
        string content,
        IReadOnlyList<ConversationAttachment> attachments)
    {
        var builder = new StringBuilder();
        builder.AppendLine(content);

        if (attachments.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Attachments:");
            foreach (var attachment in attachments)
            {
                builder.AppendLine($"- {attachment.FileName} ({attachment.ContentType}, {attachment.SizeBytes} bytes) at {attachment.StoragePath}");
            }
        }

        return builder.ToString();
    }

    private static ChatAttachment ToChatAttachment(ConversationAttachment attachment) =>
        new(attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes, attachment.StoragePath);

    private static string CreateTitle(string content)
    {
        var text = string.Join(' ', content.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(text)
            ? "New chat"
            : text.Length <= 48 ? text : text[..48] + "...";
    }
}
