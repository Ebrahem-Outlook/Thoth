using System.Text.Json;
using System.Text.RegularExpressions;
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
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Memory content cannot be empty.", nameof(content));
        }

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
        var safeLimit = Math.Clamp(limit, 0, 200);
        if (safeLimit == 0)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return await RecentAsync(scope, safeLimit, cancellationToken);
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = string.IsNullOrWhiteSpace(scope)
            ? """
              SELECT id, scope, content, created_at, metadata_json
              FROM memories
              ORDER BY created_at DESC
              LIMIT $candidateLimit;
              """
            : """
              SELECT id, scope, content, created_at, metadata_json
              FROM memories
              WHERE scope = $scope
              ORDER BY created_at DESC
              LIMIT $candidateLimit;
              """;
        command.Parameters.AddWithValue("$candidateLimit", Math.Clamp(safeLimit * 50, 200, 2000));
        if (!string.IsNullOrWhiteSpace(scope))
        {
            command.Parameters.AddWithValue("$scope", scope);
        }

        var candidates = await ReadRecordsAsync(command, cancellationToken);
        var queryTokens = Tokenize(query).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (queryTokens.Length == 0)
        {
            return candidates.Take(safeLimit).ToArray();
        }

        return candidates
            .Select(record => new { Record = record, Score = Score(query, queryTokens, record) })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Record.CreatedAt)
            .Take(safeLimit)
            .Select(item => item.Record)
            .ToArray();
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

        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 0, 200));
        if (!string.IsNullOrWhiteSpace(scope))
        {
            command.Parameters.AddWithValue("$scope", scope);
        }

        return await ReadRecordsAsync(command, cancellationToken);
    }

    private static double Score(string query, IReadOnlyList<string> queryTokens, MemoryRecord record)
    {
        var contentTokens = Tokenize(record.Content).ToArray();
        if (contentTokens.Length == 0)
        {
            return 0;
        }

        var frequencies = contentTokens
            .GroupBy(token => token, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var matched = 0;
        var frequencyScore = 0.0;

        foreach (var token in queryTokens)
        {
            if (!frequencies.TryGetValue(token, out var frequency))
            {
                continue;
            }

            matched++;
            frequencyScore += 1.0 + Math.Log(1.0 + frequency);
        }

        if (matched == 0)
        {
            return 0;
        }

        var coverage = matched / (double)queryTokens.Count;
        var exactPhrase = record.Content.Contains(query, StringComparison.OrdinalIgnoreCase) ? 4.0 : 0.0;
        var ageDays = Math.Max((DateTimeOffset.UtcNow - record.CreatedAt).TotalDays, 0);
        var recency = 0.75 / (1.0 + ageDays / 45.0);
        return exactPhrase + coverage * 6.0 + frequencyScore / queryTokens.Count + recency;
    }

    private static IEnumerable<string> Tokenize(string value) =>
        Regex.Matches(value.ToLowerInvariant(), @"[\p{L}\p{N}_-]{2,}")
            .Select(match => match.Value);

    private SqliteConnection CreateConnection() =>
        new($"Data Source={databasePath}");

    private static async Task<IReadOnlyList<MemoryRecord>> ReadRecordsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var records = new List<MemoryRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var metadataJson = reader.GetString(4);
            var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson) ??
                           new Dictionary<string, string>();

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
