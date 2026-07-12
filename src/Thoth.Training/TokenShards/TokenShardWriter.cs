using System.Security.Cryptography;
using System.Text;

namespace Thoth.Training.TokenShards;

public static class TokenShardWriter
{
    public const int CurrentVersion = 1;
    private static readonly byte[] Magic = "THTOKS1\n"u8.ToArray();

    public static async Task WriteAsync(
        string path,
        IReadOnlyList<IReadOnlyList<int>> documents,
        string tokenizerArtifactHash,
        string datasetManifestHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenizerArtifactHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetManifestHash);

        var dtype = documents.SelectMany(tokens => tokens).DefaultIfEmpty(0).Max() <= ushort.MaxValue
            ? TokenShardDType.UInt16
            : TokenShardDType.UInt32;
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            await using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Magic);
                writer.Write(CurrentVersion);
                writer.Write((byte)dtype);
                writer.Write(tokenizerArtifactHash);
                writer.Write(datasetManifestHash);
                writer.Write(documents.Sum(document => (long)document.Count));
                writer.Write(documents.Count);

                var start = 0L;
                for (var index = 0; index < documents.Count; index++)
                {
                    writer.Write($"doc-{index:000000}");
                    writer.Write(start);
                    writer.Write(documents[index].Count);
                    start += documents[index].Count;
                }

                foreach (var token in documents.SelectMany(document => document))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (token < 0)
                    {
                        throw new InvalidDataException("Token shards cannot contain negative token IDs.");
                    }

                    if (dtype == TokenShardDType.UInt16)
                    {
                        writer.Write((ushort)token);
                    }
                    else
                    {
                        writer.Write((uint)token);
                    }
                }

                await stream.FlushAsync(cancellationToken);
            }

            var checksum = await Sha256FileAsync(temporaryPath, cancellationToken);
            await using (var stream = new FileStream(temporaryPath, FileMode.Append, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(checksum, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    internal static bool IsMagic(byte[] value) => value.SequenceEqual(Magic);

    internal static int MagicLength => Magic.Length;

    private static async Task<byte[]> Sha256FileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
        return await SHA256.HashDataAsync(stream, cancellationToken);
    }
}
