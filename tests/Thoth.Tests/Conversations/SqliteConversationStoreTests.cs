using Thoth.Core.Chat;
using Thoth.Memory.Conversations;

namespace Thoth.Tests.Conversations;

public sealed class SqliteConversationStoreTests
{
    [Fact]
    public async Task AddMessageAsync_LinksAttachmentsToMessage()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "thoth-tests", Guid.NewGuid().ToString("N"), "thoth.sqlite");
        var store = new SqliteConversationStore(databasePath);
        var conversation = await store.CreateAsync("Attachment chat");
        var attachment = await store.AddAttachmentAsync("mock.png", "image/png", 120, "C:/tmp/mock.png", conversation.Id);

        var message = await store.AddMessageAsync(
            conversation.Id,
            ChatRole.User,
            "analyze this image",
            [attachment.Id]);

        var detail = await store.GetAsync(conversation.Id);

        Assert.NotNull(detail);
        Assert.Equal(message.Id, detail!.Messages.Single().Id);
        Assert.Single(detail.Messages.Single().Attachments);
        Assert.Equal("mock.png", detail.Messages.Single().Attachments.Single().FileName);
    }
}
