using Thoth.Memory.Sqlite;

namespace Thoth.Tests.Memory;

public sealed class SqliteMemoryStoreTests
{
    [Fact]
    public async Task SearchAsync_ReturnsPersistedRecords()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "thoth-tests", Guid.NewGuid().ToString("N"), "memory.sqlite");
        var store = new SqliteMemoryStore(databasePath);

        await store.AddAsync("project", "Thoth should prefer local-first memory.");
        await store.AddAsync("user", "Arabic updates are preferred.");

        var results = await store.SearchAsync("local-first", "project");

        Assert.Single(results);
        Assert.Equal("project", results[0].Scope);
        Assert.Contains("local-first", results[0].Content);
    }
}
