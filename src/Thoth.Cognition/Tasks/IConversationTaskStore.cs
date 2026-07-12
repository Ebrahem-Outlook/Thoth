namespace Thoth.Cognition.Tasks;

public interface IConversationTaskStore
{
    Task EnsureCreatedAsync(CancellationToken cancellationToken = default);

    Task<CodeGenerationTask?> GetActiveAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        CodeGenerationTask task,
        string eventType,
        Guid? messageId = null,
        CancellationToken cancellationToken = default);

    Task DeleteConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);
}

public sealed class NullConversationTaskStore : IConversationTaskStore
{
    public Task EnsureCreatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<CodeGenerationTask?> GetActiveAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        Task.FromResult<CodeGenerationTask?>(null);

    public Task SaveAsync(CodeGenerationTask task, string eventType, Guid? messageId = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task DeleteConversationAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public sealed class InMemoryConversationTaskStore : IConversationTaskStore
{
    private readonly Dictionary<Guid, CodeGenerationTask> activeTasks = new();

    public Task EnsureCreatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<CodeGenerationTask?> GetActiveAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        activeTasks.TryGetValue(conversationId, out var task);
        return Task.FromResult(task);
    }

    public Task SaveAsync(CodeGenerationTask task, string eventType, Guid? messageId = null, CancellationToken cancellationToken = default)
    {
        if (task.Status is TaskStatus.Completed or TaskStatus.Abandoned)
        {
            activeTasks.Remove(task.ConversationId);
        }
        else
        {
            activeTasks[task.ConversationId] = task;
        }

        return Task.CompletedTask;
    }

    public Task DeleteConversationAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        activeTasks.Remove(conversationId);
        return Task.CompletedTask;
    }
}
