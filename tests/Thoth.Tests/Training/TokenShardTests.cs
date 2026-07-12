using Thoth.Training.TokenShards;

namespace Thoth.Tests.Training;

public sealed class TokenShardTests
{
    [Fact]
    public async Task WriteRead_PreservesHeaderDocumentsAndShiftedTargets()
    {
        var path = TempPath();
        await TokenShardWriter.WriteAsync(
            path,
            [
                [1, 2, 3, 4],
                [10, 11]
            ],
            new string('a', 64),
            new string('b', 64));

        using var reader = await TokenShardReader.OpenAsync(path);
        var first = reader.Header.Documents[0];
        var second = reader.Header.Documents[1];
        var firstWindow = reader.ReadWindow(first, 1, contextLength: 3);
        var secondWindow = reader.ReadWindow(second, 0, contextLength: 3);

        Assert.Equal(TokenShardDType.UInt16, reader.Header.DType);
        Assert.Equal(6, reader.Header.TokenCount);
        Assert.Equal([2, 3, 4], firstWindow.Inputs);
        Assert.Equal([3, 4, -100], firstWindow.Targets);
        Assert.Equal([true, true, false], firstWindow.TargetMask);
        Assert.Equal([10, 11, 0], secondWindow.Inputs);
        Assert.Equal([11, -100, -100], secondWindow.Targets);
    }

    [Fact]
    public async Task Open_DetectsCorruptedShard()
    {
        var path = TempPath();
        await TokenShardWriter.WriteAsync(path, [[1, 2, 3]], new string('a', 64), new string('b', 64));
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            stream.Position = stream.Length - 1;
            var value = stream.ReadByte();
            stream.Position = stream.Length - 1;
            stream.WriteByte((byte)(value ^ 0xFF));
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => TokenShardReader.OpenAsync(path));
    }

    [Fact]
    public async Task Dataset_OrderIsDeterministicBySeedAndEpoch()
    {
        var path = TempPath();
        await TokenShardWriter.WriteAsync(
            path,
            Enumerable.Range(0, 8)
                .Select(index => (IReadOnlyList<int>)[index * 10, index * 10 + 1, index * 10 + 2, index * 10 + 3])
                .ToArray(),
            new string('a', 64),
            new string('b', 64));

        using var reader = await TokenShardReader.OpenAsync(path);
        var dataset = new TokenWindowDataset(reader, contextLength: 2, stride: 1, seed: 99);

        var epoch0 = dataset.CreateEpochOrder(0);
        var epoch0Again = dataset.CreateEpochOrder(0);
        var epoch1 = dataset.CreateEpochOrder(1);

        Assert.Equal(epoch0, epoch0Again);
        Assert.NotEqual(epoch0, epoch1);
    }

    [Fact]
    public async Task Writer_UsesUInt32WhenVocabularyExceedsUInt16()
    {
        var path = TempPath();
        await TokenShardWriter.WriteAsync(path, [[70_000, 70_001]], new string('a', 64), new string('b', 64));

        using var reader = await TokenShardReader.OpenAsync(path);

        Assert.Equal(TokenShardDType.UInt32, reader.Header.DType);
        Assert.Equal(70_000, reader.ReadToken(0));
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "thoth-tests", Guid.NewGuid().ToString("N"), "tokens.thtok");
}
