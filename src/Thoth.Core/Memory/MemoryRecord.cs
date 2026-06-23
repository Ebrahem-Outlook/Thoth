namespace Thoth.Core.Memory;

public sealed record MemoryRecord(
    Guid Id,
    string Scope,
    string Content,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, string> Metadata);
