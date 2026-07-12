using System.Text;
using System.Text.Json;
using Thoth.Cognition.Concepts;
using Thoth.Cognition.Procedures;
using Thoth.Cognition.Tasks;
using Thoth.Core.Agent;
using Thoth.Core.Chat;
using Thoth.Core.Understanding;

namespace Thoth.Core.Conversations;

public sealed class ChatOrchestrator(
    IConversationStore conversations,
    IUserUnderstandingService understanding,
    AgentEngine agentEngine,
    IChatModel chatModel,
    IConversationTaskStore? conversationTasks = null,
    CodeTaskExtractor? codeTasks = null,
    TaskContinuationResolver? continuationResolver = null,
    TaskMerger? taskMerger = null,
    ProcedureRegistry? procedures = null)
{
    private readonly IConversationTaskStore conversationTasks = conversationTasks ?? new InMemoryConversationTaskStore();
    private readonly CodeTaskExtractor codeTasks = codeTasks ?? new CodeTaskExtractor();
    private readonly TaskContinuationResolver continuationResolver = continuationResolver ?? new TaskContinuationResolver();
    private readonly TaskMerger taskMerger = taskMerger ?? new TaskMerger();
    private readonly ProcedureRegistry procedures = procedures ?? new ProcedureRegistry();

    public async Task<ChatTurnResult> SendAsync(
        ChatTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        await conversations.EnsureCreatedAsync(cancellationToken);
        await conversationTasks.EnsureCreatedAsync(cancellationToken);

        var conversation = request.ConversationId is Guid existingId
            ? (await conversations.GetAsync(existingId, cancellationToken))?.Conversation
            : null;

        conversation ??= await conversations.CreateAsync(CreateTitle(request.Content), cancellationToken: cancellationToken);

        var attachments = request.AttachmentIds.Count == 0
            ? []
            : await conversations.GetAttachmentsAsync(request.AttachmentIds, cancellationToken);

        var activeTask = await conversationTasks.GetActiveAsync(conversation.Id, cancellationToken);
        var codeTask = BuildCodeTask(conversation.Id, request.Content, activeTask);
        var activeTaskSummary = SummarizeTask(codeTask ?? activeTask);

        var understood = await understanding.UnderstandAsync(
            new UnderstandingRequest(
                request.Content,
                attachments.Select(attachment => attachment.ContentType).ToArray(),
                conversation.Project,
                activeTaskSummary),
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
        AssistantResponseKind assistantKind;
        IReadOnlyList<string> suggestedDetails = [];
        string? responseTaskSummary = activeTaskSummary;

        if (codeTask is not null)
        {
            var taskResponse = await HandleCodeTaskAsync(codeTask, userMessage, request.Content, cancellationToken);
            assistantText = taskResponse.Content;
            assistantKind = taskResponse.Kind;
            suggestedDetails = taskResponse.SuggestedDetails;
            responseTaskSummary = taskResponse.ActiveTaskSummary;
        }
        else if (request.UseTools && understood.RequiresTools)
        {
            var goal = BuildAgentGoal(request.Content, attachments);
            agentRun = await agentEngine.RunAsync(
                new AgentRequest(goal, request.WorkingDirectory, request.Model, request.MaxSteps),
                cancellationToken);
            assistantText = agentRun.FinalAnswer;
            assistantKind = AssistantResponseKind.ToolResultSummary;
        }
        else
        {
            assistantText = await ReplyDirectlyAsync(request, conversation.Id, attachments, understood, cancellationToken);
            assistantKind = AssistantResponseKind.DirectAnswer;
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
                agentRunId = agentRun?.RunId,
                assistantKind = assistantKind.ToString(),
                suggestedDetails,
                activeTaskSummary = responseTaskSummary
            }),
            cancellationToken: cancellationToken);

        var detail = await conversations.GetAsync(conversation.Id, cancellationToken)
                     ?? new ConversationDetail(conversation, [userMessage, assistantMessage]);

        return new ChatTurnResult(detail, userMessage, assistantMessage, understood, agentRun, assistantKind, suggestedDetails, responseTaskSummary);
    }

    private CodeGenerationTask? BuildCodeTask(
        Guid conversationId,
        string content,
        CodeGenerationTask? activeTask)
    {
        if (codeTasks.IsRepositoryBound(content))
        {
            return null;
        }

        if (activeTask is not null && continuationResolver.IsContinuation(activeTask, content))
        {
            return taskMerger.Merge(activeTask, content);
        }

        return codeTasks.ExtractNewTask(conversationId, content);
    }

    private async Task<AssistantTaskResponse> HandleCodeTaskAsync(
        CodeGenerationTask task,
        ConversationMessage userMessage,
        string requestContent,
        CancellationToken cancellationToken)
    {
        var withTurn = taskMerger.AddTurn(task, userMessage.Id, requestContent);
        await conversationTasks.SaveAsync(withTurn, "task.updated", userMessage.Id, cancellationToken);

        if (!withTurn.IsReady)
        {
            return new AssistantTaskResponse(
                AssistantResponseKind.Clarification,
                TaskResponseComposer.CreateClarification(withTurn, requestContent),
                withTurn.MissingSlots,
                SummarizeTask(withTurn));
        }

        if (!procedures.TryExecute(withTurn, out var generated))
        {
            return new AssistantTaskResponse(
                AssistantResponseKind.CapabilityLimitation,
                "I understood the coding task, but I do not have a verified local procedure for that exact behavior yet.",
                [],
                SummarizeTask(withTurn));
        }

        var completed = withTurn with
        {
            Status = Thoth.Cognition.Tasks.TaskStatus.Completed,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = withTurn.Version + 1
        };
        await conversationTasks.SaveAsync(completed, "task.completed", userMessage.Id, cancellationToken);
        return new AssistantTaskResponse(
            AssistantResponseKind.DirectAnswer,
            generated.Content,
            [],
            SummarizeTask(completed));
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

        var currentTurnAlreadyLoaded =
            detail?.Messages.LastOrDefault() is { Role: ChatRole.User } last &&
            string.Equals(last.Content, request.Content, StringComparison.Ordinal);
        if (!currentTurnAlreadyLoaded)
        {
            history.Add(new ChatMessage(ChatRole.User, request.Content, Attachments: attachments.Select(ToChatAttachment).ToArray()));
        }

        var system = new ChatMessage(ChatRole.System, $$"""
        You are Thoth, a capable Arabic/English AI agent.
        Understand the user's exact intent before answering.
        Be direct, useful, and honest about what you can and cannot inspect.
        Current intent: {{understood.Intent}}
        Topic: {{understood.Topic}}
        Language: {{understood.Language}}
        """);

        var response = await chatModel.CompleteAsync(
            new ChatRequest(
                [system, .. history],
                request.Model,
                0.25,
                Purpose: ModelRequestPurpose.DirectReply,
                Input: new DirectReplyModelInput(
                    request.Content,
                    attachments.Any(attachment => attachment.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)),
                    understood.Language,
                    attachments.Select(ToChatAttachment).ToArray(),
                    SummarizeTask(await conversationTasks.GetActiveAsync(conversationId, cancellationToken)))),
            cancellationToken);

        return response.Content.Trim();
    }

    private static string? SummarizeTask(CodeGenerationTask? task)
    {
        if (task is null)
        {
            return null;
        }

        var missing = task.MissingSlots.Count == 0 ? "none" : string.Join(", ", task.MissingSlots);
        return $"Code task: {task.Language.DisplayName()} {task.ArtifactKind} for {task.Behavior ?? "unspecified behavior"}; status {task.Status}; missing {missing}.";
    }

    private sealed record AssistantTaskResponse(
        AssistantResponseKind Kind,
        string Content,
        IReadOnlyList<string> SuggestedDetails,
        string? ActiveTaskSummary);

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
