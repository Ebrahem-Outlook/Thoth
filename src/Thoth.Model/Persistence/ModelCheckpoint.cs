using System.Text;

namespace Thoth.Model.Persistence;

public static class ModelCheckpoint
{
    private const string Magic = "THOTH-RNN";
    private const int FormatVersion = 1;

    public static async Task SaveAsync(
        string path,
        RecurrentLanguageModel model,
        bool includeOptimizer = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(model);

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        var state = model.ExportState(includeOptimizer);

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             1024 * 64,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

                writer.Write(Magic);
                writer.Write(FormatVersion);
                writer.Write(state.Config.VocabularySize);
                writer.Write(state.Config.EmbeddingSize);
                writer.Write(state.Config.HiddenSize);
                writer.Write(state.Config.SequenceLength);
                writer.Write(state.Config.Seed);
                writer.Write(state.OptimizerStep);

                WriteArray(writer, state.Embeddings);
                WriteArray(writer, state.InputWeights);
                WriteArray(writer, state.RecurrentWeights);
                WriteArray(writer, state.HiddenBias);
                WriteArray(writer, state.OutputWeights);
                WriteArray(writer, state.OutputBias);
                WriteArray(writer, state.EmbeddingsFirstMoment);
                WriteArray(writer, state.EmbeddingsSecondMoment);
                WriteArray(writer, state.InputWeightsFirstMoment);
                WriteArray(writer, state.InputWeightsSecondMoment);
                WriteArray(writer, state.RecurrentWeightsFirstMoment);
                WriteArray(writer, state.RecurrentWeightsSecondMoment);
                WriteArray(writer, state.HiddenBiasFirstMoment);
                WriteArray(writer, state.HiddenBiasSecondMoment);
                WriteArray(writer, state.OutputWeightsFirstMoment);
                WriteArray(writer, state.OutputWeightsSecondMoment);
                WriteArray(writer, state.OutputBiasFirstMoment);
                WriteArray(writer, state.OutputBiasSecondMoment);

                writer.Flush();
                await stream.FlushAsync(cancellationToken);
            }

            // The stream must be closed before the atomic replacement, especially on Windows.
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static async Task<RecurrentLanguageModel> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 64,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var magic = reader.ReadString();
        if (!string.Equals(magic, Magic, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Not a Thoth neural checkpoint.");
        }

        var version = reader.ReadInt32();
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Unsupported checkpoint version: {version}.");
        }

        var config = new NeuralModelConfig(
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32());
        var optimizerStep = reader.ReadInt64();

        var state = new RecurrentModelState(
            config,
            optimizerStep,
            ReadArray(reader),
            ReadArray(reader),
            ReadArray(reader),
            ReadArray(reader),
            ReadArray(reader),
            ReadArray(reader),
            ReadArray(reader),
            ReadArray(reader),
            ReadArray(reader),
            ReadArray(reader),
            ReadArray(reader),
            ReadArray(reader),
            ReadArray(reader),
            ReadArray(reader),
            ReadArray(reader),
            ReadArray(reader),
            ReadArray(reader),
            ReadArray(reader));

        await Task.CompletedTask.WaitAsync(cancellationToken);
        return RecurrentLanguageModel.FromState(state);
    }

    private static void WriteArray(BinaryWriter writer, double[] values)
    {
        writer.Write(values.Length);
        foreach (var value in values)
        {
            writer.Write(value);
        }
    }

    private static double[] ReadArray(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length < 0 || length > 500_000_000)
        {
            throw new InvalidDataException($"Invalid checkpoint array length: {length}.");
        }

        var values = new double[length];
        for (var index = 0; index < length; index++)
        {
            values[index] = reader.ReadDouble();
        }

        return values;
    }
}
