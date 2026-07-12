namespace Thoth.Training.TokenShards;

public enum TokenShardDType : byte
{
    UInt16 = 1,
    UInt32 = 2
}

public sealed record TokenShardDocument(
    string DocumentId,
    long StartToken,
    int TokenCount);

public sealed record TokenShardHeader(
    int Version,
    TokenShardDType DType,
    string TokenizerArtifactHash,
    string DatasetManifestHash,
    long TokenCount,
    IReadOnlyList<TokenShardDocument> Documents,
    long TokenDataOffset,
    byte[] Checksum);

public sealed record TokenWindow(
    int[] Inputs,
    int[] Targets,
    bool[] TargetMask,
    string DocumentId,
    int DocumentOffset);
