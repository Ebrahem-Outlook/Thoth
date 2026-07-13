using System.Text.Json;
using Thoth.Model;
using Thoth.Model.Persistence;

namespace Thoth.Training.Torch;

public sealed record TorchCheckpointManifest(
    int Version,
    string Architecture,
    long OptimizerStep,
    string RunId,
    DateTimeOffset CreatedUtc,
    TorchTrainingOptions Options,
    string ModelFile);

public static class TorchCheckpointDirectory
{
    public static async Task<string> SaveAsync(
        string runDirectory,
        TorchTransformerLanguageModel model,
        TorchTrainingOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);

        var checkpointName = $"step-{model.OptimizerStep:00000000}";
        var finalDirectory = Path.Combine(Path.GetFullPath(runDirectory), "checkpoints", checkpointName);
        var tempDirectory = finalDirectory + ".tmp-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.GetDirectoryName(finalDirectory)!);

        try
        {
            Directory.CreateDirectory(tempDirectory);
            await TorchTransformerCheckpoint.SaveAsync(Path.Combine(tempDirectory, "model.bin"), model, cancellationToken);
            var manifest = new TorchCheckpointManifest(
                1,
                "torch-decoder-only-transformer-v1",
                model.OptimizerStep,
                options.RunId,
                DateTimeOffset.UtcNow,
                options,
                "model.bin");
            await File.WriteAllTextAsync(
                Path.Combine(tempDirectory, "checkpoint.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }),
                cancellationToken);

            await MoveDirectoryWithRetryAsync(tempDirectory, finalDirectory, cancellationToken);
            return finalDirectory;
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private static async Task MoveDirectoryWithRetryAsync(
        string tempDirectory,
        string finalDirectory,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(finalDirectory))
                {
                    Directory.Delete(finalDirectory, recursive: true);
                }

                Directory.Move(tempDirectory, finalDirectory);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                await Task.Delay(150, cancellationToken);
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                await Task.Delay(150, cancellationToken);
            }
        }

        if (Directory.Exists(finalDirectory))
        {
            Directory.Delete(finalDirectory, recursive: true);
        }

        Directory.Move(tempDirectory, finalDirectory);
    }
}
