using Thoth.Model;
using Thoth.Model.Persistence;
using Thoth.Training.TokenShards;
using Thoth.Training.Torch;

namespace Thoth.Tests.Training;

public sealed class TorchTransformerTrainerTests
{
    [Fact]
    public async Task TrainAsync_UsesGradientAccumulationAndWritesCheckpoint()
    {
        using var model = new TorchTransformerLanguageModel(new TorchTransformerConfig(
            VocabularySize: 18,
            ContextLength: 8,
            LayerCount: 1,
            Width: 32,
            HeadCount: 4,
            FeedForwardSize: 64,
            Dropout: 0,
            Seed: 7));
        var windows = new[]
        {
            Window([1, 2, 3, 4], [2, 3, 4, 5]),
            Window([2, 3, 4, 5], [3, 4, 5, 6])
        };
        var runDir = Path.Combine(Path.GetTempPath(), "thoth-tests", Guid.NewGuid().ToString("N"), "run");

        var report = await new TorchTransformerTrainer(model).TrainAsync(
            windows,
            new TorchTrainingOptions
            {
                MaxOptimizerSteps = 1,
                GradientAccumulationSteps = 2,
                LearningRate = 0.01,
                MinimumLearningRate = 0.001,
                WarmupSteps = 0,
                WeightDecay = 0,
                CheckpointEverySteps = 1,
                RunId = "test-run"
            },
            runDir);

        var checkpoint = Path.Combine(runDir, "checkpoints", "step-00000001", "model.bin");
        Assert.Equal(0, report.StartingStep);
        Assert.Equal(1, report.CompletedStep);
        Assert.True(File.Exists(Path.Combine(runDir, "train.jsonl")));
        Assert.True(File.Exists(checkpoint));
        using var loaded = await TorchTransformerCheckpoint.LoadAsync(checkpoint);
        Assert.Equal(1, loaded.OptimizerStep);
    }

    private static TokenWindow Window(int[] inputs, int[] targets) =>
        new(inputs, targets, targets.Select(target => target >= 0).ToArray(), "doc", 0);
}
