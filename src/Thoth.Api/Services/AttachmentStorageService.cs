using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Thoth.Core.Configuration;
using Thoth.Core.Conversations;

namespace Thoth.Api.Services;

public sealed class AttachmentStorageService(
    IConversationStore conversations,
    IOptions<ThothOptions> options)
{
    private static readonly Regex UnsafeFileNameCharacters = new(@"[^a-zA-Z0-9._-]+", RegexOptions.Compiled);

    public async Task<ConversationAttachment> SaveAsync(
        IFormFile file,
        Guid? conversationId = null,
        CancellationToken cancellationToken = default)
    {
        if (file.Length <= 0)
        {
            throw new InvalidOperationException("Uploaded file is empty.");
        }

        var dataDirectory = Path.IsPathFullyQualified(options.Value.DataDirectory)
            ? Path.GetFullPath(options.Value.DataDirectory)
            : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, options.Value.DataDirectory));

        var uploadDirectory = Path.Combine(dataDirectory, "uploads");
        Directory.CreateDirectory(uploadDirectory);

        var safeName = SanitizeFileName(file.FileName);
        var storagePath = Path.Combine(uploadDirectory, $"{Guid.NewGuid():N}_{safeName}");

        await using (var stream = File.Create(storagePath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        return await conversations.AddAttachmentAsync(
            safeName,
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            file.Length,
            storagePath,
            conversationId,
            cancellationToken: cancellationToken);
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return "upload.bin";
        }

        name = UnsafeFileNameCharacters.Replace(name, "_");
        return name.Length <= 120 ? name : name[^120..];
    }
}
