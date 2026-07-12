using System.Text;

namespace Thoth.Model.Persistence;

public static class TorchTransformerCheckpoint
{
    private const string Magic = "THOTH-TORCH-TRANSFORMER";
    private const int FormatVersion = 1;

    public static async Task SaveAsync(
        string path,
        TorchTransformerLanguageModel model,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(model);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        var state = model.ExportState();

        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            {
                using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
                writer.Write(Magic);
                writer.Write(FormatVersion);
                WriteConfig(writer, state.Config);
                writer.Write(state.OptimizerStep);
                writer.Write(state.Parameters.Count);
                foreach (var parameter in state.Parameters)
                {
                    writer.Write(parameter.Name);
                    writer.Write(parameter.Shape.Length);
                    foreach (var dimension in parameter.Shape)
                    {
                        writer.Write(dimension);
                    }

                    WriteArray(writer, parameter.Value);
                    WriteArray(writer, parameter.FirstMoment);
                    WriteArray(writer, parameter.SecondMoment);
                }

                writer.Flush();
                await stream.FlushAsync(cancellationToken);
            }

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

    public static async Task<TorchTransformerLanguageModel> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var magic = reader.ReadString();
        if (!string.Equals(magic, Magic, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Not a Thoth Torch Transformer checkpoint.");
        }

        var version = reader.ReadInt32();
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Unsupported Torch Transformer checkpoint version: {version}.");
        }

        var config = ReadConfig(reader);
        var optimizerStep = reader.ReadInt64();
        var count = reader.ReadInt32();
        var parameters = new List<TorchParameterSnapshot>(count);
        for (var index = 0; index < count; index++)
        {
            var name = reader.ReadString();
            var rank = reader.ReadInt32();
            var shape = new long[rank];
            for (var dimension = 0; dimension < rank; dimension++)
            {
                shape[dimension] = reader.ReadInt64();
            }

            parameters.Add(new TorchParameterSnapshot(
                name,
                shape,
                ReadArray(reader),
                ReadArray(reader),
                ReadArray(reader)));
        }

        await Task.CompletedTask.WaitAsync(cancellationToken);
        return TorchTransformerLanguageModel.FromState(new TorchTransformerState(config, optimizerStep, parameters));
    }

    private static void WriteConfig(BinaryWriter writer, TorchTransformerConfig config)
    {
        writer.Write(config.VocabularySize);
        writer.Write(config.ContextLength);
        writer.Write(config.LayerCount);
        writer.Write(config.Width);
        writer.Write(config.HeadCount);
        writer.Write(config.FeedForwardSize);
        writer.Write(config.Dropout);
        writer.Write(config.Seed);
        writer.Write(config.PaddingToken);
        writer.Write(config.Device);
        writer.Write(config.TieOutputEmbeddings);
    }

    private static TorchTransformerConfig ReadConfig(BinaryReader reader) =>
        new(
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadDouble(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadString(),
            reader.ReadBoolean());

    private static void WriteArray(BinaryWriter writer, float[] values)
    {
        writer.Write(values.Length);
        foreach (var value in values)
        {
            writer.Write(value);
        }
    }

    private static float[] ReadArray(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length < 0 || length > 500_000_000)
        {
            throw new InvalidDataException($"Invalid Torch checkpoint array length: {length}.");
        }

        var values = new float[length];
        for (var index = 0; index < length; index++)
        {
            values[index] = reader.ReadSingle();
        }

        return values;
    }
}
