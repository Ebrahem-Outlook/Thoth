using Thoth.Cognition.Tasks;
using Thoth.Memory.Cognition;
using Thoth.Memory.Conversations;

namespace Thoth.Tests.Memory;

public sealed class SqliteConversationTaskStoreTests
{
    [Fact]
    public async Task SaveAsync_PersistsActiveTaskAcrossStoreInstances()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var conversationStore = new SqliteConversationStore(databasePath);
            var conversation = await conversationStore.CreateAsync("Task test");
            var task = new CodeTaskExtractor().ExtractNewTask(conversation.Id, "build C++ method calculator");
            var store = new SqliteConversationTaskStore(databasePath);

            await store.SaveAsync(task!, "task.created");
            var reloaded = await new SqliteConversationTaskStore(databasePath).GetActiveAsync(conversation.Id);

            Assert.NotNull(reloaded);
            Assert.Equal(task!.Id, reloaded!.Id);
            Assert.Equal(Thoth.Cognition.Tasks.TaskStatus.Ready, reloaded.Status);
        }
        finally
        {
            TryDelete(databasePath);
        }
    }

    [Fact]
    public async Task DeleteConversation_RemovesTaskThroughCascade()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var conversationStore = new SqliteConversationStore(databasePath);
            var conversation = await conversationStore.CreateAsync("Task cascade");
            var taskStore = new SqliteConversationTaskStore(databasePath);
            var task = new CodeTaskExtractor().ExtractNewTask(conversation.Id, "build C# method");

            await taskStore.SaveAsync(task!, "task.created");
            await conversationStore.DeleteAsync(conversation.Id);

            var active = await taskStore.GetActiveAsync(conversation.Id);
            Assert.Null(active);
        }
        finally
        {
            TryDelete(databasePath);
        }
    }

    private static string CreateTempDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"thoth-task-store-{Guid.NewGuid():N}.sqlite");

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
