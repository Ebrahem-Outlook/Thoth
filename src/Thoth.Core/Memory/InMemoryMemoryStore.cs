using System.Collections.Concurrent;

namespace Thoth.Core.Memory;

public sealed class InMemoryMemoryStore : IMemoryStore
{
    private readonly ConcurrentQueue<MemoryRecord> records = new();

    public Task EnsureCreatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<MemoryRecord> AddAsync(
        string scope,
        string content,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var record = new MemoryRecord(
            Guid.NewGuid(),
            scope,
            content,
            DateTimeOffset.UtcNow,
            metadata ?? new Dictionary<string, string>());

        records.Enqueue(record);
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<MemoryRecord>> SearchAsync(
        string query,
        string? scope = null,
        int limit = 8,
        CancellationToken cancellationToken = default)
    {
        var results = records
            .Where(record => MatchesScope(record, scope))
            .Where(record =>
                string.IsNullOrWhiteSpace(query) ||
                record.Content.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                record.Metadata.Values.Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(record => record.CreatedAt)
            .Take(Math.Max(limit, 0))
            .ToArray();

        return Task.FromResult<IReadOnlyList<MemoryRecord>>(results);
    }

    public Task<IReadOnlyList<MemoryRecord>> RecentAsync(
        string? scope = null,
        int limit = 8,
        CancellationToken cancellationToken = default)
    {
        var results = records
            .Where(record => MatchesScope(record, scope))
            .OrderByDescending(record => record.CreatedAt)
            .Take(Math.Max(limit, 0))
            .ToArray();

        return Task.FromResult<IReadOnlyList<MemoryRecord>>(results);
    }

    private static bool MatchesScope(MemoryRecord record, string? scope) =>
        string.IsNullOrWhiteSpace(scope) || string.Equals(record.Scope, scope, StringComparison.OrdinalIgnoreCase);
}
