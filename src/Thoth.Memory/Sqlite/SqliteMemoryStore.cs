using System.Text.Json;
using Microsoft.Data.Sqlite;
using Thoth.Core.Memory;

namespace Thoth.Memory.Sqlite;

public sealed class SqliteMemoryStore(string databasePath) : IMemoryStore
{
    private readonly string databasePath = Path.GetFullPath(databasePath);

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
        CREATE TABLE IF NOT EXISTS memories (
            id TEXT PRIMARY KEY,
            scope TEXT NOT NULL,
            content TEXT NOT NULL,
            created_at TEXT NOT NULL,
            metadata_json TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_memories_scope_created_at
        ON memories(scope, created_at DESC);
        """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<MemoryRecord> AddAsync(
        string scope,
        string content,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);

        var record = new MemoryRecord(
            Guid.NewGuid(),
            string.IsNullOrWhiteSpace(scope) ? "project" : scope.Trim(),
            content.Trim(),
            DateTimeOffset.UtcNow,
            metadata ?? new Dictionary<string, string>());

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
        INSERT INTO memories (id, scope, content, created_at, metadata_json)
        VALUES ($id, $scope, $content, $createdAt, $metadataJson);
        """;
        command.Parameters.AddWithValue("$id", record.Id.ToString("N"));
        command.Parameters.AddWithValue("$scope", record.Scope);
        command.Parameters.AddWithValue("$content", record.Content);
        command.Parameters.AddWithValue("$createdAt", record.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$metadataJson", JsonSerializer.Serialize(record.Metadata));

        await command.ExecuteNonQueryAsync(cancellationToken);
        return record;
    }

    public async Task<IReadOnlyList<MemoryRecord>> SearchAsync(
        string query,
        string? scope = null,
        int limit = 8,
        CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        var where = string.IsNullOrWhiteSpace(scope)
            ? "content LIKE $query"
            : "scope = $scope AND content LIKE $query";

        command.CommandText = $"""
        SELECT id, scope, content, created_at, metadata_json
        FROM memories
        WHERE {where}
        ORDER BY created_at DESC
        LIMIT $limit;
        """;

        command.Parameters.AddWithValue("$query", $"%{query}%");
        command.Parameters.AddWithValue("$limit", Math.Max(limit, 0));
        if (!string.IsNullOrWhiteSpace(scope))
        {
            command.Parameters.AddWithValue("$scope", scope);
        }

        return await ReadRecordsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryRecord>> RecentAsync(
        string? scope = null,
        int limit = 8,
        CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = string.IsNullOrWhiteSpace(scope)
            ? """
              SELECT id, scope, content, created_at, metadata_json
              FROM memories
              ORDER BY created_at DESC
              LIMIT $limit;
              """
            : """
              SELECT id, scope, content, created_at, metadata_json
              FROM memories
              WHERE scope = $scope
              ORDER BY created_at DESC
              LIMIT $limit;
              """;

        command.Parameters.AddWithValue("$limit", Math.Max(limit, 0));
        if (!string.IsNullOrWhiteSpace(scope))
        {
            command.Parameters.AddWithValue("$scope", scope);
        }

        return await ReadRecordsAsync(command, cancellationToken);
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={databasePath}");
    }

    private static async Task<IReadOnlyList<MemoryRecord>> ReadRecordsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var records = new List<MemoryRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var metadataJson = reader.GetString(4);
            var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson) ?? new Dictionary<string, string>();

            records.Add(new MemoryRecord(
                Guid.ParseExact(reader.GetString(0), "N"),
                reader.GetString(1),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3)),
                metadata));
        }

        return records;
    }
}
