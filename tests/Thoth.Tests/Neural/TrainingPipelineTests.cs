using Thoth.Model;
using Thoth.Tokenization;
using Thoth.Training;

namespace Thoth.Tests.Neural;

public sealed class TrainingPipelineTests
{
    [Fact]
    public async Task TrainAsync_WritesResumableCheckpoint()
    {
        var tokenizer = new ByteTokenizer();
        var model = new RecurrentLanguageModel(new NeuralModelConfig(
            tokenizer.VocabularySize,
            EmbeddingSize: 8,
            HiddenSize: 12,
            SequenceLength: 8,
            Seed: 31));
        var corpus = tokenizer.Encode("abcabcabcabcabcabcabcabc", addBeginningOfSequence: true, addEndOfSequence: true);
        var checkpoint = Path.Combine(Path.GetTempPath(), "thoth-tests", Guid.NewGuid().ToString("N"), "pipeline.bin");
        var trainer = new LanguageModelTrainer(model, tokenizer);

        var report = await trainer.TrainAsync(
            corpus,
            new TrainingOptions
            {
                Epochs = 1,
                StepsPerEpoch = 4,
                SequenceLength = 8,
                LearningRate = 0.005,
                MinimumLearningRate = 0.001,
                WarmupSteps = 0,
                CheckpointEverySteps = 0,
                WeightDecay = 0
            },
            checkpoint);

        Assert.True(File.Exists(checkpoint));
        Assert.Equal(4, report.CompletedStep - report.StartingStep);
        Assert.True(double.IsFinite(report.FinalLoss));
    }
}
