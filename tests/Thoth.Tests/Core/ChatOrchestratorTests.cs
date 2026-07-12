using Thoth.Core.Agent;
using Thoth.Core.Chat;
using Thoth.Core.Conversations;
using Thoth.Core.Memory;
using Thoth.Core.Sandbox;
using Thoth.Core.Tools;
using Thoth.Core.Understanding;

namespace Thoth.Tests.Core;

public sealed class ChatOrchestratorTests
{
    [Fact]
    public async Task SendAsync_NewConversationSendsCurrentUserMessageOnce()
    {
        var store = new FakeConversationStore();
        var model = new CapturingChatModel();
        var orchestrator = CreateOrchestrator(store, model);

        await orchestrator.SendAsync(new ChatTurnRequest(
            null,
            "Hello",
            [],
            Directory.GetCurrentDirectory(),
            "thoth-self",
            UseTools: false));

        Assert.NotNull(model.LastRequest);
        Assert.Single(model.LastRequest!.Messages.Where(message => message.Role == ChatRole.User && message.Content == "Hello"));
    }

    [Fact]
    public async Task SendAsync_ExistingConversationPreservesOrderAndCurrentMessageOnce()
    {
        var store = new FakeConversationStore();
        var conversation = await store.CreateAsync("Existing");
        await store.AddMessageAsync(conversation.Id, ChatRole.User, "First");
        await store.AddMessageAsync(conversation.Id, ChatRole.Assistant, "First answer");
        var model = new CapturingChatModel();
        var orchestrator = CreateOrchestrator(store, model);

        await orchestrator.SendAsync(new ChatTurnRequest(
            conversation.Id,
            "Second",
            [],
            Directory.GetCurrentDirectory(),
            "thoth-self",
            UseTools: false));

        var nonSystem = model.LastRequest!.Messages.Where(message => message.Role != ChatRole.System).ToArray();
        Assert.Equal(["First", "First answer", "Second"], nonSystem.Select(message => message.Content).ToArray());
        Assert.Single(nonSystem.Where(message => message.Role == ChatRole.User && message.Content == "Second"));
    }

    [Fact]
    public async Task SendAsync_AttachmentsStayOnCurrentUserMessage()
    {
        var store = new FakeConversationStore();
        var attachment = await store.AddAttachmentAsync("image.png", "image/png", 12, "uploads/image.png");
        var model = new CapturingChatModel();
        var orchestrator = CreateOrchestrator(store, model);

        await orchestrator.SendAsync(new ChatTurnRequest(
            null,
            "describe this",
            [attachment.Id],
            Directory.GetCurrentDirectory(),
            "thoth-self",
            UseTools: false));

        var current = Assert.Single(model.LastRequest!.Messages.Where(message => message.Role == ChatRole.User));
        var captured = Assert.Single(current.Attachments ?? []);
        Assert.Equal(attachment.Id, captured.Id);
        Assert.Equal("image/png", captured.ContentType);
    }

    private static ChatOrchestrator CreateOrchestrator(FakeConversationStore store, CapturingChatModel model)
    {
        var engine = new AgentEngine(
            model,
            new HeuristicAgentDecisionService(),
            new ToolRegistry(),
            new InMemoryMemoryStore(),
            new AllowAllPolicy());
        return new ChatOrchestrator(store, new HeuristicUnderstandingService(), engine, model);
    }

    private sealed class CapturingChatModel : IChatModel
    {
        public ChatRequest? LastRequest { get; private set; }

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ChatResponse("ok", request.Model));
        }
    }

    private sealed class AllowAllPolicy : IExecutionPolicy
    {
        public PolicyDecision Authorize(ToolInvocation invocation, ToolContext context) => PolicyDecision.Allow();
    }

    private sealed class FakeConversationStore : IConversationStore
    {
        private readonly Dictionary<Guid, Conversation> conversations = new();
        private readonly Dictionary<Guid, List<ConversationMessage>> messages = new();
        private readonly Dictionary<Guid, ConversationAttachment> attachments = new();

        public Task EnsureCreatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<Conversation>> ListAsync(
            string? query = null,
            string? project = null,
            bool includeArchived = false,
            int limit = 100,
            CancellationToken cancellationToken = default) =>
            Task.FromResult((IReadOnlyList<Conversation>)conversations.Values.Take(limit).ToArray());

        public Task<Conversation> CreateAsync(
            string title,
            string? project = null,
            CancellationToken cancellationToken = default)
        {
            var conversation = new Conversation(
                Guid.NewGuid(),
                title,
                project,
                false,
                false,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                0);
            conversations[conversation.Id] = conversation;
            messages[conversation.Id] = [];
            return Task.FromResult(conversation);
        }

        public Task<ConversationDetail?> GetAsync(Guid conversationId, CancellationToken cancellationToken = default)
        {
            if (!conversations.TryGetValue(conversationId, out var conversation))
            {
                return Task.FromResult<ConversationDetail?>(null);
            }

            return Task.FromResult<ConversationDetail?>(new ConversationDetail(conversation, messages[conversationId].ToArray()));
        }

        public Task<Conversation?> UpdateAsync(
            Guid conversationId,
            string? title = null,
            bool? isPinned = null,
            bool? isArchived = null,
            string? project = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(conversations.GetValueOrDefault(conversationId));

        public Task<bool> DeleteAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(conversations.Remove(conversationId));

        public Task<ConversationMessage> AddMessageAsync(
            Guid conversationId,
            ChatRole role,
            string content,
            IReadOnlyList<Guid>? attachmentIds = null,
            string? intent = null,
            string? metadataJson = null,
            CancellationToken cancellationToken = default)
        {
            var linked = attachmentIds is null
                ? []
                : attachmentIds
                    .Where(attachments.ContainsKey)
                    .Select(id => attachments[id] with { ConversationId = conversationId })
                    .ToArray();
            var message = new ConversationMessage(
                Guid.NewGuid(),
                conversationId,
                role,
                content,
                DateTimeOffset.UtcNow,
                linked,
                intent,
                metadataJson);
            messages[conversationId].Add(message);
            conversations[conversationId] = conversations[conversationId] with { MessageCount = messages[conversationId].Count };
            return Task.FromResult(message);
        }

        public Task<ConversationAttachment> AddAttachmentAsync(
            string fileName,
            string contentType,
            long sizeBytes,
            string storagePath,
            Guid? conversationId = null,
            Guid? messageId = null,
            CancellationToken cancellationToken = default)
        {
            var attachment = new ConversationAttachment(
                Guid.NewGuid(),
                conversationId,
                messageId,
                fileName,
                contentType,
                sizeBytes,
                storagePath,
                DateTimeOffset.UtcNow);
            attachments[attachment.Id] = attachment;
            return Task.FromResult(attachment);
        }

        public Task<IReadOnlyList<ConversationAttachment>> GetAttachmentsAsync(
            IReadOnlyList<Guid> attachmentIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult((IReadOnlyList<ConversationAttachment>)attachmentIds.Where(attachments.ContainsKey).Select(id => attachments[id]).ToArray());

        public Task<ConversationAttachment?> GetAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(attachments.GetValueOrDefault(attachmentId));
    }
}
