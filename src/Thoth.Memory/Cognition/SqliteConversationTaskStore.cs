using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Thoth.Cognition.Tasks;

namespace Thoth.Memory.Cognition;

public sealed class SqliteConversationTaskStore(string databasePath) : IConversationTaskStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string databasePath = Path.GetFullPath(databasePath);

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
        CREATE TABLE IF NOT EXISTS conversation_tasks (
            id TEXT PRIMARY KEY,
            conversation_id TEXT NOT NULL,
            status TEXT NOT NULL,
            kind TEXT NOT NULL,
            payload_json TEXT NOT NULL,
            version INTEGER NOT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            FOREIGN KEY (conversation_id) REFERENCES conversations(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS conversation_task_events (
            id TEXT PRIMARY KEY,
            task_id TEXT NOT NULL,
            conversation_id TEXT NOT NULL,
            message_id TEXT NULL,
            event_type TEXT NOT NULL,
            payload_json TEXT NOT NULL,
            created_at TEXT NOT NULL,
            FOREIGN KEY (task_id) REFERENCES conversation_tasks(id) ON DELETE CASCADE,
            FOREIGN KEY (conversation_id) REFERENCES conversations(id) ON DELETE CASCADE,
            FOREIGN KEY (message_id) REFERENCES conversation_messages(id) ON DELETE SET NULL
        );

        CREATE INDEX IF NOT EXISTS idx_conversation_tasks_active
        ON conversation_tasks(conversation_id, status, updated_at DESC);

        CREATE INDEX IF NOT EXISTS idx_conversation_task_events_task
        ON conversation_task_events(task_id, created_at);
        """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<CodeGenerationTask?> GetActiveAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
        SELECT payload_json
        FROM conversation_tasks
        WHERE conversation_id = $conversationId
          AND status NOT IN ('Completed', 'Abandoned')
        ORDER BY updated_at DESC
        LIMIT 1;
        """;
        command.Parameters.AddWithValue("$conversationId", ToId(conversationId));

        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        return string.IsNullOrWhiteSpace(payload)
            ? null
            : JsonSerializer.Deserialize<CodeGenerationTask>(payload, JsonOptions);
    }

    public async Task SaveAsync(
        CodeGenerationTask task,
        string eventType,
        Guid? messageId = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var payload = JsonSerializer.Serialize(task, JsonOptions);
        var upsert = connection.CreateCommand();
        upsert.Transaction = (SqliteTransaction)transaction;
        upsert.CommandText = """
        INSERT INTO conversation_tasks
            (id, conversation_id, status, kind, payload_json, version, created_at, updated_at)
        VALUES
            ($id, $conversationId, $status, $kind, $payloadJson, $version, $createdAt, $updatedAt)
        ON CONFLICT(id) DO UPDATE SET
            status = excluded.status,
            payload_json = excluded.payload_json,
            version = excluded.version,
            updated_at = excluded.updated_at;
        """;
        upsert.Parameters.AddWithValue("$id", ToId(task.Id));
        upsert.Parameters.AddWithValue("$conversationId", ToId(task.ConversationId));
        upsert.Parameters.AddWithValue("$status", task.Status.ToString());
        upsert.Parameters.AddWithValue("$kind", "code_generation");
        upsert.Parameters.AddWithValue("$payloadJson", payload);
        upsert.Parameters.AddWithValue("$version", task.Version);
        upsert.Parameters.AddWithValue("$createdAt", task.CreatedAt.ToString("O"));
        upsert.Parameters.AddWithValue("$updatedAt", task.UpdatedAt.ToString("O"));
        await upsert.ExecuteNonQueryAsync(cancellationToken);

        var insertEvent = connection.CreateCommand();
        insertEvent.Transaction = (SqliteTransaction)transaction;
        insertEvent.CommandText = """
        INSERT INTO conversation_task_events
            (id, task_id, conversation_id, message_id, event_type, payload_json, created_at)
        VALUES
            ($id, $taskId, $conversationId, $messageId, $eventType, $payloadJson, $createdAt);
        """;
        insertEvent.Parameters.AddWithValue("$id", ToId(Guid.NewGuid()));
        insertEvent.Parameters.AddWithValue("$taskId", ToId(task.Id));
        insertEvent.Parameters.AddWithValue("$conversationId", ToId(task.ConversationId));
        insertEvent.Parameters.AddWithValue("$messageId", messageId is null ? DBNull.Value : ToId(messageId.Value));
        insertEvent.Parameters.AddWithValue("$eventType", eventType);
        insertEvent.Parameters.AddWithValue("$payloadJson", payload);
        insertEvent.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        await insertEvent.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteConversationAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
        DELETE FROM conversation_task_events WHERE conversation_id = $conversationId;
        DELETE FROM conversation_tasks WHERE conversation_id = $conversationId;
        """;
        command.Parameters.AddWithValue("$conversationId", ToId(conversationId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqliteConnection CreateConnection() =>
        new($"Data Source={databasePath};Foreign Keys=True;Pooling=False");

    private static string ToId(Guid id) => id.ToString("N");
}
