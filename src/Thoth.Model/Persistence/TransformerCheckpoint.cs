using System.Text;

namespace Thoth.Model.Persistence;

public static class TransformerCheckpoint
{
    private const string Magic = "THOTH-TRANSFORMER";
    private const int FormatVersion = 1;

    public const string CurrentFormat = Magic;

    public const int CurrentFormatVersion = FormatVersion;

    public static async Task SaveAsync(
        string path,
        TransformerLanguageModel model,
        bool includeOptimizer = true,
        string tokenizer = ModelCheckpointMetadata.ByteTokenizer,
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
                writer.Write(state.Config.ContextLength);
                writer.Write(state.Config.LayerCount);
                writer.Write(state.Config.Width);
                writer.Write(state.Config.HeadCount);
                writer.Write(state.Config.FeedForwardSize);
                writer.Write(state.Config.Dropout);
                writer.Write(state.Config.Seed);
                writer.Write(state.Config.TieOutputEmbeddings);
                writer.Write(state.OptimizerStep);

                WriteArray(writer, state.TokenEmbeddings);
                WriteArray(writer, state.FinalNormWeight);
                WriteArray(writer, state.LmHead);
                WriteArray(writer, state.OutputBias);
                WriteArray(writer, state.LmHeadFirstMoment);
                WriteArray(writer, state.LmHeadSecondMoment);
                WriteArray(writer, state.OutputBiasFirstMoment);
                WriteArray(writer, state.OutputBiasSecondMoment);

                writer.Write(state.Layers.Count);
                foreach (var layer in state.Layers)
                {
                    WriteArray(writer, layer.AttentionNormWeight);
                    WriteArray(writer, layer.FeedForwardNormWeight);
                    WriteArray(writer, layer.QueryWeight);
                    WriteArray(writer, layer.KeyWeight);
                    WriteArray(writer, layer.ValueWeight);
                    WriteArray(writer, layer.OutputWeight);
                    WriteArray(writer, layer.GateWeight);
                    WriteArray(writer, layer.UpWeight);
                    WriteArray(writer, layer.DownWeight);
                }

                writer.Flush();
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
            await ModelCheckpointQualityGate.SaveMetadataAsync(
                fullPath,
                ModelCheckpointMetadata.CreateUnqualified(model, tokenizer: tokenizer),
                cancellationToken);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static async Task<TransformerLanguageModel> LoadAsync(
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
            throw new InvalidDataException("Not a Thoth Transformer checkpoint.");
        }

        var version = reader.ReadInt32();
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Unsupported Transformer checkpoint version: {version}.");
        }

        var config = new TransformerConfig(
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadDouble(),
            reader.ReadInt32(),
            reader.ReadBoolean());
        var optimizerStep = reader.ReadInt64();

        var tokenEmbeddings = ReadArray(reader);
        var finalNormWeight = ReadArray(reader);
        var lmHead = ReadArray(reader);
        var outputBias = ReadArray(reader);
        var lmHeadFirstMoment = ReadArray(reader);
        var lmHeadSecondMoment = ReadArray(reader);
        var outputBiasFirstMoment = ReadArray(reader);
        var outputBiasSecondMoment = ReadArray(reader);

        var layerCount = reader.ReadInt32();
        if (layerCount != config.LayerCount)
        {
            throw new InvalidDataException("Transformer checkpoint layer count does not match the config.");
        }

        var layers = new List<TransformerLayerState>(layerCount);
        for (var index = 0; index < layerCount; index++)
        {
            layers.Add(new TransformerLayerState(
                ReadArray(reader),
                ReadArray(reader),
                ReadArray(reader),
                ReadArray(reader),
                ReadArray(reader),
                ReadArray(reader),
                ReadArray(reader),
                ReadArray(reader),
                ReadArray(reader)));
        }

        await Task.CompletedTask.WaitAsync(cancellationToken);
        return TransformerLanguageModel.FromState(new TransformerModelState(
            config,
            optimizerStep,
            tokenEmbeddings,
            finalNormWeight,
            lmHead,
            outputBias,
            lmHeadFirstMoment,
            lmHeadSecondMoment,
            outputBiasFirstMoment,
            outputBiasSecondMoment,
            layers));
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

