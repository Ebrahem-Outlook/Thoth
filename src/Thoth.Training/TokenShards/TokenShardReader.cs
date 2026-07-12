using System.Security.Cryptography;
using System.Text;

namespace Thoth.Training.TokenShards;

public sealed class TokenShardReader : IDisposable
{
    private readonly FileStream stream;
    private readonly BinaryReader reader;
    private readonly int bytesPerToken;

    private TokenShardReader(FileStream stream, BinaryReader reader, TokenShardHeader header)
    {
        this.stream = stream;
        this.reader = reader;
        Header = header;
        bytesPerToken = header.DType == TokenShardDType.UInt16 ? 2 : 4;
    }

    public TokenShardHeader Header { get; }

    public static async Task<TokenShardReader> OpenAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await ValidateChecksumAsync(path, cancellationToken);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var magic = reader.ReadBytes(TokenShardWriter.MagicLength);
        if (!TokenShardWriter.IsMagic(magic))
        {
            throw new InvalidDataException("Invalid token shard magic.");
        }

        var version = reader.ReadInt32();
        if (version != TokenShardWriter.CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported token shard version: {version}.");
        }

        var dtype = (TokenShardDType)reader.ReadByte();
        var tokenizerHash = reader.ReadString();
        var datasetHash = reader.ReadString();
        var tokenCount = reader.ReadInt64();
        var documentCount = reader.ReadInt32();
        var documents = new List<TokenShardDocument>(documentCount);
        for (var index = 0; index < documentCount; index++)
        {
            documents.Add(new TokenShardDocument(
                reader.ReadString(),
                reader.ReadInt64(),
                reader.ReadInt32()));
        }

        var tokenDataOffset = stream.Position;
        var checksum = new byte[32];
        stream.Seek(-32, SeekOrigin.End);
        _ = stream.Read(checksum, 0, checksum.Length);
        stream.Position = tokenDataOffset;

        return new TokenShardReader(
            stream,
            reader,
            new TokenShardHeader(version, dtype, tokenizerHash, datasetHash, tokenCount, documents, tokenDataOffset, checksum));
    }

    public int ReadToken(long index)
    {
        if (index < 0 || index >= Header.TokenCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        stream.Position = Header.TokenDataOffset + index * bytesPerToken;
        return Header.DType == TokenShardDType.UInt16
            ? reader.ReadUInt16()
            : checked((int)reader.ReadUInt32());
    }

    public TokenWindow ReadWindow(
        TokenShardDocument document,
        int documentOffset,
        int contextLength,
        int paddingToken = 0,
        int ignoredTarget = -100)
    {
        if (contextLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(contextLength));
        }

        var inputs = new int[contextLength];
        var targets = new int[contextLength];
        var mask = new bool[contextLength];
        Array.Fill(inputs, paddingToken);
        Array.Fill(targets, ignoredTarget);

        for (var index = 0; index < contextLength; index++)
        {
            var inputOffset = documentOffset + index;
            var targetOffset = inputOffset + 1;
            if (inputOffset < document.TokenCount)
            {
                inputs[index] = ReadToken(document.StartToken + inputOffset);
            }

            if (targetOffset < document.TokenCount)
            {
                targets[index] = ReadToken(document.StartToken + targetOffset);
                mask[index] = true;
            }
        }

        return new TokenWindow(inputs, targets, mask, document.DocumentId, documentOffset);
    }

    public void Dispose()
    {
        reader.Dispose();
        stream.Dispose();
    }

    private static async Task ValidateChecksumAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
        if (stream.Length < 32)
        {
            throw new InvalidDataException("Token shard is too small to contain a checksum.");
        }

        var dataLength = stream.Length - 32;
        using var sha = SHA256.Create();
        var buffer = new byte[64 * 1024];
        var remaining = dataLength;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of token shard while validating checksum.");
            }

            sha.TransformBlock(buffer, 0, read, null, 0);
            remaining -= read;
        }

        sha.TransformFinalBlock([], 0, 0);
        var expected = new byte[32];
        var checksumRead = await stream.ReadAsync(expected, cancellationToken);
        if (checksumRead != expected.Length || !sha.Hash!.SequenceEqual(expected))
        {
            throw new InvalidDataException("Token shard checksum mismatch.");
        }
    }
}
