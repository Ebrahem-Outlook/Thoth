namespace Thoth.Core.Memory;

public interface IMemoryStore
{
    Task EnsureCreatedAsync(CancellationToken cancellationToken = default);

    Task<MemoryRecord> AddAsync(
        string scope,
        string content,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemoryRecord>> SearchAsync(
        string query,
        string? scope = null,
        int limit = 8,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemoryRecord>> RecentAsync(
        string? scope = null,
        int limit = 8,
        CancellationToken cancellationToken = default);
}
