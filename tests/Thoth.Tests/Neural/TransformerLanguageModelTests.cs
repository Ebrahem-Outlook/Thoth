using Thoth.Inference;
using Thoth.Model;
using Thoth.Model.Persistence;
using Thoth.Tokenization;
using Thoth.Training;

namespace Thoth.Tests.Neural;

public sealed class TransformerLanguageModelTests
{
    [Fact]
    public void Forward_CpuShapeTest_ReturnsBatchSequenceVocabularyLogits()
    {
        var model = CreateCpuModel(vocabularySize: 24);
        var inputs = Matrix([1, 2, 3, 4], [4, 3, 2, 1]);

        var result = model.Forward(inputs);

        Assert.Equal(2, result.Logits.GetLength(0));
        Assert.Equal(4, result.Logits.GetLength(1));
        Assert.Equal(24, result.Logits.GetLength(2));
        Assert.All(Flatten(result.Logits), AssertFinite);
    }

    [Fact]
    public void Forward_CpuCausalTest_FutureTokenDoesNotChangeEarlierLogits()
    {
        var model = CreateCpuModel(vocabularySize: 24, seed: 42);
        var first = Matrix([1, 2, 3, 4, 5, 6]);
        var second = Matrix([1, 2, 3, 4, 9, 8]);

        var firstLogits = model.Forward(first).Logits;
        var secondLogits = model.Forward(second).Logits;

        for (var position = 0; position < 4; position++)
        {
            for (var token = 0; token < model.VocabularySize; token++)
            {
                Assert.Equal(firstLogits[0, position, token], secondLogits[0, position, token], precision: 10);
            }
        }
    }

    [Fact]
    public void TrainBatch_CpuOneBatchOverfit_DropsLossSubstantially()
    {
        var model = CreateCpuModel(vocabularySize: 16, seed: 7);
        var inputs = Matrix([1, 2, 3, 4, 5, 6, 7, 8]);
        var targets = Matrix([5, 5, 5, 5, 5, 5, 5, 5]);
        var initial = model.EvaluateBatch(inputs, targets);

        for (var step = 0; step < 60; step++)
        {
            model.TrainBatch(inputs, targets, learningRate: 0.05, weightDecay: 0, gradientClip: 5);
        }

        var final = model.EvaluateBatch(inputs, targets);
        Assert.True(final < initial * 0.45, $"Expected one-batch overfit. Initial={initial}, Final={final}");
    }

    [Fact]
    public void TrainBatch_CpuLearningTest_ReducesLossOnDeterministicFixture()
    {
        var model = CreateCpuModel(vocabularySize: 18, seed: 17);
        var inputs = Matrix([1, 2, 1, 2, 1, 2]);
        var targets = Matrix([2, 1, 2, 1, 2, 1]);
        var initial = model.EvaluateBatch(inputs, targets);

        for (var step = 0; step < 80; step++)
        {
            model.TrainBatch(inputs, targets, learningRate: 0.03, weightDecay: 0, gradientClip: 5);
        }

        var final = model.EvaluateBatch(inputs, targets);
        Assert.True(final < initial, $"Expected learning to reduce loss. Initial={initial}, Final={final}");
    }

    [Fact]
    public async Task TransformerCheckpoint_CpuRoundTrip_PreservesLogits()
    {
        var model = CreateCpuModel(vocabularySize: 20, seed: 91);
        var inputs = Matrix([1, 2, 3, 4]);
        var before = model.Forward(inputs).Logits;
        var checkpoint = TempPath("transformer.bin");

        await TransformerCheckpoint.SaveAsync(checkpoint, model);
        var loaded = await TransformerCheckpoint.LoadAsync(checkpoint);
        var after = loaded.Forward(inputs).Logits;

        for (var row = 0; row < before.GetLength(0); row++)
        {
            for (var position = 0; position < before.GetLength(1); position++)
            {
                for (var token = 0; token < before.GetLength(2); token++)
                {
                    Assert.Equal(before[row, position, token], after[row, position, token], precision: 10);
                }
            }
        }
    }

    [Fact]
    public async Task TransformerTrainer_CpuResumeTest_ContinuesStoredOptimizerStep()
    {
        var checkpoint = TempPath("resume-transformer.bin");
        var corpus = Enumerable.Repeat(new[] { 1, 2, 3, 4, 5, 6 }, 8).SelectMany(x => x).ToArray();
        var model = CreateCpuModel(vocabularySize: 16, seed: 12);
        var trainer = new TransformerLanguageModelTrainer(model);

        await trainer.TrainAsync(
            corpus,
            new TrainingOptions
            {
                Epochs = 1,
                StepsPerEpoch = 2,
                SequenceLength = 4,
                BatchSize = 1,
                LearningRate = 0.02,
                MinimumLearningRate = 0.01,
                WarmupSteps = 0,
                WeightDecay = 0,
                CheckpointEverySteps = 0
            },
            checkpoint);

        var loaded = await TransformerCheckpoint.LoadAsync(checkpoint);
        Assert.Equal(2, loaded.OptimizerStep);

        var resumed = await new TransformerLanguageModelTrainer(loaded).TrainAsync(
            corpus,
            new TrainingOptions
            {
                Epochs = 1,
                StepsPerEpoch = 1,
                SequenceLength = 4,
                BatchSize = 1,
                LearningRate = 0.02,
                MinimumLearningRate = 0.01,
                WarmupSteps = 0,
                WeightDecay = 0,
                CheckpointEverySteps = 0
            },
            checkpoint);

        Assert.Equal(3, resumed.CompletedStep);
    }

    [Fact]
    public async Task BpeTokenizer_CpuRoundTrip_ArabicEnglishCodeAndPunctuation()
    {
        const string text = "Thoth بيفهم UTF-8، code: const x = (a + b) => `${a}!`;";
        var tokenizer = BpeTokenizer.Train(
            [
                text,
                "English punctuation: hello, world!",
                "Arabic: ازيك يا معلم؟",
                "C# code: public int Add(int a, int b) => a + b;"
            ],
            targetVocabularySize: 300);
        var tokens = tokenizer.Encode(text, addBeginningOfSequence: true, addEndOfSequence: true);
        var decoded = tokenizer.Decode(tokens);
        var path = TempPath("tokenizer.json");

        await tokenizer.SaveAsync(path);
        var loaded = await BpeTokenizer.LoadAsync(path);
        var loadedDecoded = loaded.Decode(loaded.Encode(text, addBeginningOfSequence: true, addEndOfSequence: true));

        Assert.Equal(text, decoded);
        Assert.Equal(text, loadedDecoded);
        Assert.True(tokenizer.VocabularySize <= 300);
    }

    [Fact]
    public void TransformerGeneration_CpuSmoke_RespectsMaxTokenCount()
    {
        var tokenizer = new ByteTokenizer();
        var model = new TransformerLanguageModel(new TransformerConfig(
            tokenizer.VocabularySize,
            ContextLength: 16,
            LayerCount: 1,
            Width: 16,
            HeadCount: 4,
            FeedForwardSize: 32,
            Dropout: 0,
            Seed: 5));
        var generator = new TransformerTextGenerator(model, tokenizer);

        var tokens = generator.GenerateTokenIds(
            "hello",
            new GenerationOptions { MaxNewTokens = 6, Temperature = 0.9, TopK = 8, TopP = 0.9, Seed = 1 });

        Assert.True(tokens.Count <= 6);
        Assert.All(tokens, token => Assert.InRange(token, 0, tokenizer.VocabularySize - 1));
    }

    [Fact]
    public void TrainBatch_CpuNoNanTest_FailsClearlyOnNonFiniteParameters()
    {
        var model = CreateCpuModel(vocabularySize: 16, seed: 21);
        var state = model.ExportState();
        state.LmHead[0] = double.NaN;
        model.ImportState(state);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            model.TrainBatch(
                Matrix([1, 2, 3, 4]),
                Matrix([2, 3, 4, 5]),
                learningRate: 0.01));

        Assert.Contains("non-finite", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TransformerCpuSmoke_MandatorySuiteRequiresNoGpu()
    {
        var model = CreateCpuModel(vocabularySize: 12, seed: 3);
        var loss = model.TrainBatch(
            Matrix([1, 2, 3]),
            Matrix([2, 3, 4]),
            learningRate: 0.01,
            weightDecay: 0);

        Assert.True(double.IsFinite(loss));
    }

    private static TransformerLanguageModel CreateCpuModel(int vocabularySize, int seed = 1) =>
        new(new TransformerConfig(
            vocabularySize,
            ContextLength: 16,
            LayerCount: 1,
            Width: 16,
            HeadCount: 4,
            FeedForwardSize: 32,
            Dropout: 0,
            Seed: seed));

    private static int[,] Matrix(params int[][] rows)
    {
        var result = new int[rows.Length, rows[0].Length];
        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < rows[row].Length; column++)
            {
                result[row, column] = rows[row][column];
            }
        }

        return result;
    }

    private static IEnumerable<double> Flatten(double[,,] values)
    {
        for (var row = 0; row < values.GetLength(0); row++)
        {
            for (var position = 0; position < values.GetLength(1); position++)
            {
                for (var token = 0; token < values.GetLength(2); token++)
                {
                    yield return values[row, position, token];
                }
            }
        }
    }

    private static void AssertFinite(double value) => Assert.True(double.IsFinite(value));

    private static string TempPath(string fileName) =>
        Path.Combine(Path.GetTempPath(), "thoth-transformer-tests", Guid.NewGuid().ToString("N"), fileName);
}

