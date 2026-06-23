namespace Thoth.Core.Chat;

public sealed record ChatAttachment(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string StoragePath);
