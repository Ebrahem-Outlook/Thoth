using System.Text.Json;
using Microsoft.Data.Sqlite;
using Thoth.Core.Chat;
using Thoth.Core.Conversations;

namespace Thoth.Memory.Conversations;

public sealed class SqliteConversationStore(string databasePath) : IConversationStore
{
    private readonly string databasePath = Path.GetFullPath(databasePath);

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
        CREATE TABLE IF NOT EXISTS conversations (
            id TEXT PRIMARY KEY,
            title TEXT NOT NULL,
            project TEXT NULL,
            is_pinned INTEGER NOT NULL,
            is_archived INTEGER NOT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS conversation_messages (
            id TEXT PRIMARY KEY,
            conversation_id TEXT NOT NULL,
            role TEXT NOT NULL,
            content TEXT NOT NULL,
            created_at TEXT NOT NULL,
            intent TEXT NULL,
            metadata_json TEXT NULL,
            FOREIGN KEY (conversation_id) REFERENCES conversations(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS conversation_attachments (
            id TEXT PRIMARY KEY,
            conversation_id TEXT NULL,
            message_id TEXT NULL,
            file_name TEXT NOT NULL,
            content_type TEXT NOT NULL,
            size_bytes INTEGER NOT NULL,
            storage_path TEXT NOT NULL,
            created_at TEXT NOT NULL,
            FOREIGN KEY (conversation_id) REFERENCES conversations(id) ON DELETE CASCADE,
            FOREIGN KEY (message_id) REFERENCES conversation_messages(id) ON DELETE SET NULL
        );

        CREATE INDEX IF NOT EXISTS idx_conversations_updated_at
        ON conversations(updated_at DESC);

        CREATE INDEX IF NOT EXISTS idx_messages_conversation_created_at
        ON conversation_messages(conversation_id, created_at);
        """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Conversation>> ListAsync(
        string? query = null,
        string? project = null,
        bool includeArchived = false,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var filters = new List<string>();
        if (!includeArchived)
        {
            filters.Add("c.is_archived = 0");
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            filters.Add("(c.title LIKE $query OR EXISTS (SELECT 1 FROM conversation_messages m WHERE m.conversation_id = c.id AND m.content LIKE $query))");
        }

        if (!string.IsNullOrWhiteSpace(project))
        {
            filters.Add("c.project = $project");
        }

        var where = filters.Count == 0 ? "" : "WHERE " + string.Join(" AND ", filters);
        var command = connection.CreateCommand();
        command.CommandText = $"""
        SELECT c.id, c.title, c.project, c.is_pinned, c.is_archived, c.created_at, c.updated_at,
               (SELECT COUNT(*) FROM conversation_messages m WHERE m.conversation_id = c.id) AS message_count
        FROM conversations c
        {where}
        ORDER BY c.is_pinned DESC, c.updated_at DESC
        LIMIT $limit;
        """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        if (!string.IsNullOrWhiteSpace(query))
        {
            command.Parameters.AddWithValue("$query", $"%{query}%");
        }

        if (!string.IsNullOrWhiteSpace(project))
        {
            command.Parameters.AddWithValue("$project", project);
        }

        return await ReadConversationListAsync(command, cancellationToken);
    }

    public async Task<Conversation> CreateAsync(
        string title,
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(
            Guid.NewGuid(),
            string.IsNullOrWhiteSpace(title) ? "New chat" : title.Trim(),
            string.IsNullOrWhiteSpace(project) ? null : project.Trim(),
            false,
            false,
            now,
            now,
            0);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
        INSERT INTO conversations (id, title, project, is_pinned, is_archived, created_at, updated_at)
        VALUES ($id, $title, $project, 0, 0, $createdAt, $updatedAt);
        """;
        command.Parameters.AddWithValue("$id", ToId(conversation.Id));
        command.Parameters.AddWithValue("$title", conversation.Title);
        command.Parameters.AddWithValue("$project", (object?)conversation.Project ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", conversation.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", conversation.UpdatedAt.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        return conversation;
    }

    public async Task<ConversationDetail?> GetAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var conversation = await GetConversationAsync(connection, conversationId, cancellationToken);
        if (conversation is null)
        {
            return null;
        }

        var messages = await GetMessagesAsync(connection, conversationId, cancellationToken);
        return new ConversationDetail(conversation, messages);
    }

    public async Task<Conversation?> UpdateAsync(
        Guid conversationId,
        string? title = null,
        bool? isPinned = null,
        bool? isArchived = null,
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var updates = new List<string> { "updated_at = $updatedAt" };
        var command = connection.CreateCommand();
        command.Parameters.AddWithValue("$id", ToId(conversationId));
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));

        if (title is not null)
        {
            updates.Add("title = $title");
            command.Parameters.AddWithValue("$title", title);
        }

        if (isPinned is not null)
        {
            updates.Add("is_pinned = $isPinned");
            command.Parameters.AddWithValue("$isPinned", isPinned.Value ? 1 : 0);
        }

        if (isArchived is not null)
        {
            updates.Add("is_archived = $isArchived");
            command.Parameters.AddWithValue("$isArchived", isArchived.Value ? 1 : 0);
        }

        if (project is not null)
        {
            updates.Add("project = $project");
            command.Parameters.AddWithValue("$project", string.IsNullOrWhiteSpace(project) ? DBNull.Value : project);
        }

        command.CommandText = $"UPDATE conversations SET {string.Join(", ", updates)} WHERE id = $id;";
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await GetConversationAsync(connection, conversationId, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM conversations WHERE id = $id;";
        command.Parameters.AddWithValue("$id", ToId(conversationId));
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<ConversationMessage> AddMessageAsync(
        Guid conversationId,
        ChatRole role,
        string content,
        IReadOnlyList<Guid>? attachmentIds = null,
        string? intent = null,
        string? metadataJson = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        var messageId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
        INSERT INTO conversation_messages (id, conversation_id, role, content, created_at, intent, metadata_json)
        VALUES ($id, $conversationId, $role, $content, $createdAt, $intent, $metadataJson);
        """;
        insert.Parameters.AddWithValue("$id", ToId(messageId));
        insert.Parameters.AddWithValue("$conversationId", ToId(conversationId));
        insert.Parameters.AddWithValue("$role", role.ToString());
        insert.Parameters.AddWithValue("$content", content);
        insert.Parameters.AddWithValue("$createdAt", now.ToString("O"));
        insert.Parameters.AddWithValue("$intent", (object?)intent ?? DBNull.Value);
        insert.Parameters.AddWithValue("$metadataJson", (object?)metadataJson ?? DBNull.Value);
        await insert.ExecuteNonQueryAsync(cancellationToken);

        if (attachmentIds is not null && attachmentIds.Count > 0)
        {
            foreach (var attachmentId in attachmentIds)
            {
                var updateAttachment = connection.CreateCommand();
                updateAttachment.Transaction = transaction;
                updateAttachment.CommandText = """
                UPDATE conversation_attachments
                SET conversation_id = $conversationId, message_id = $messageId
                WHERE id = $attachmentId;
                """;
                updateAttachment.Parameters.AddWithValue("$conversationId", ToId(conversationId));
                updateAttachment.Parameters.AddWithValue("$messageId", ToId(messageId));
                updateAttachment.Parameters.AddWithValue("$attachmentId", ToId(attachmentId));
                await updateAttachment.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        var touch = connection.CreateCommand();
        touch.Transaction = transaction;
        touch.CommandText = "UPDATE conversations SET updated_at = $updatedAt WHERE id = $conversationId;";
        touch.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        touch.Parameters.AddWithValue("$conversationId", ToId(conversationId));
        await touch.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        var attachments = attachmentIds is null || attachmentIds.Count == 0
            ? []
            : await GetAttachmentsAsync(attachmentIds, cancellationToken);

        return new ConversationMessage(
            messageId,
            conversationId,
            role,
            content,
            now,
            attachments,
            intent,
            metadataJson);
    }

    public async Task<ConversationAttachment> AddAttachmentAsync(
        string fileName,
        string contentType,
        long sizeBytes,
        string storagePath,
        Guid? conversationId = null,
        Guid? messageId = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        var attachment = new ConversationAttachment(
            Guid.NewGuid(),
            conversationId,
            messageId,
            fileName,
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            sizeBytes,
            storagePath,
            DateTimeOffset.UtcNow);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
        INSERT INTO conversation_attachments
        (id, conversation_id, message_id, file_name, content_type, size_bytes, storage_path, created_at)
        VALUES ($id, $conversationId, $messageId, $fileName, $contentType, $sizeBytes, $storagePath, $createdAt);
        """;
        command.Parameters.AddWithValue("$id", ToId(attachment.Id));
        command.Parameters.AddWithValue("$conversationId", conversationId is null ? DBNull.Value : ToId(conversationId.Value));
        command.Parameters.AddWithValue("$messageId", messageId is null ? DBNull.Value : ToId(messageId.Value));
        command.Parameters.AddWithValue("$fileName", attachment.FileName);
        command.Parameters.AddWithValue("$contentType", attachment.ContentType);
        command.Parameters.AddWithValue("$sizeBytes", attachment.SizeBytes);
        command.Parameters.AddWithValue("$storagePath", attachment.StoragePath);
        command.Parameters.AddWithValue("$createdAt", attachment.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return attachment;
    }

    public async Task<IReadOnlyList<ConversationAttachment>> GetAttachmentsAsync(
        IReadOnlyList<Guid> attachmentIds,
        CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        if (attachmentIds.Count == 0)
        {
            return [];
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        var placeholders = attachmentIds.Select((_, index) => $"$id{index}").ToArray();
        command.CommandText = $"""
        SELECT id, conversation_id, message_id, file_name, content_type, size_bytes, storage_path, created_at
        FROM conversation_attachments
        WHERE id IN ({string.Join(", ", placeholders)});
        """;

        for (var index = 0; index < attachmentIds.Count; index++)
        {
            command.Parameters.AddWithValue(placeholders[index], ToId(attachmentIds[index]));
        }

        return await ReadAttachmentsAsync(command, cancellationToken);
    }

    public async Task<ConversationAttachment?> GetAttachmentAsync(
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        var attachments = await GetAttachmentsAsync([attachmentId], cancellationToken);
        return attachments.FirstOrDefault();
    }

    private SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection($"Data Source={databasePath};Foreign Keys=True;Pooling=False");
        return connection;
    }

    private static async Task<Conversation?> GetConversationAsync(
        SqliteConnection connection,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
        SELECT c.id, c.title, c.project, c.is_pinned, c.is_archived, c.created_at, c.updated_at,
               (SELECT COUNT(*) FROM conversation_messages m WHERE m.conversation_id = c.id) AS message_count
        FROM conversations c
        WHERE c.id = $id;
        """;
        command.Parameters.AddWithValue("$id", ToId(conversationId));
        var list = await ReadConversationListAsync(command, cancellationToken);
        return list.FirstOrDefault();
    }

    private static async Task<IReadOnlyList<ConversationMessage>> GetMessagesAsync(
        SqliteConnection connection,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var messagesCommand = connection.CreateCommand();
        messagesCommand.CommandText = """
        SELECT id, conversation_id, role, content, created_at, intent, metadata_json
        FROM conversation_messages
        WHERE conversation_id = $conversationId
        ORDER BY created_at ASC;
        """;
        messagesCommand.Parameters.AddWithValue("$conversationId", ToId(conversationId));

        var messages = new List<ConversationMessage>();
        await using var reader = await messagesCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(new ConversationMessage(
                ParseId(reader.GetString(0)),
                ParseId(reader.GetString(1)),
                Enum.Parse<ChatRole>(reader.GetString(2), ignoreCase: true),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4)),
                [],
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        if (messages.Count == 0)
        {
            return messages;
        }

        var attachmentCommand = connection.CreateCommand();
        attachmentCommand.CommandText = """
        SELECT id, conversation_id, message_id, file_name, content_type, size_bytes, storage_path, created_at
        FROM conversation_attachments
        WHERE conversation_id = $conversationId
        ORDER BY created_at ASC;
        """;
        attachmentCommand.Parameters.AddWithValue("$conversationId", ToId(conversationId));
        var attachments = await ReadAttachmentsAsync(attachmentCommand, cancellationToken);
        var grouped = attachments
            .Where(attachment => attachment.MessageId is not null)
            .GroupBy(attachment => attachment.MessageId!.Value)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ConversationAttachment>)group.ToArray());

        return messages
            .Select(message => message with
            {
                Attachments = grouped.TryGetValue(message.Id, out var messageAttachments) ? messageAttachments : []
            })
            .ToArray();
    }

    private static async Task<IReadOnlyList<Conversation>> ReadConversationListAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var conversations = new List<Conversation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            conversations.Add(new Conversation(
                ParseId(reader.GetString(0)),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetInt32(3) == 1,
                reader.GetInt32(4) == 1,
                DateTimeOffset.Parse(reader.GetString(5)),
                DateTimeOffset.Parse(reader.GetString(6)),
                reader.GetInt32(7)));
        }

        return conversations;
    }

    private static async Task<IReadOnlyList<ConversationAttachment>> ReadAttachmentsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var attachments = new List<ConversationAttachment>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            attachments.Add(new ConversationAttachment(
                ParseId(reader.GetString(0)),
                reader.IsDBNull(1) ? null : ParseId(reader.GetString(1)),
                reader.IsDBNull(2) ? null : ParseId(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt64(5),
                reader.GetString(6),
                DateTimeOffset.Parse(reader.GetString(7))));
        }

        return attachments;
    }

    private static string ToId(Guid id) => id.ToString("N");

    private static Guid ParseId(string id) => Guid.ParseExact(id, "N");
}
