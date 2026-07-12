using TorchSharp;
using Thoth.Model;
using Thoth.Model.Persistence;

namespace Thoth.Tests.Neural;

public sealed class TorchTransformerLanguageModelTests
{
    [Fact]
    public void Backward_AllParametersReceiveFiniteGradients()
    {
        using var model = CreateModel();
        var loss = model.Loss(
            model.TensorFrom(new long[,] { { 1, 2, 3, 4 }, { 4, 3, 2, 1 } }),
            model.TensorFrom(new long[,] { { 2, 3, 4, 5 }, { 3, 2, 1, 0 } }));

        loss.backward();

        var gradients = model.ParameterGradientFiniteMap();
        Assert.NotEmpty(gradients);
        Assert.All(gradients, item => Assert.True(item.Value, item.Key));
    }

    [Fact]
    public void TrainBatch_UpdatesEmbeddingAttentionAndFeedForwardWeights()
    {
        using var model = CreateModel();
        var embedding = model.CloneParameter("token_embedding");
        var attention = model.CloneParameter("layers.0.wq");
        var feedForward = model.CloneParameter("layers.0.w_gate");

        model.TrainBatch(
            new long[,] { { 1, 2, 3, 4 }, { 4, 3, 2, 1 } },
            new long[,] { { 2, 3, 4, 5 }, { 3, 2, 1, 0 } },
            learningRate: 0.01,
            weightDecay: 0,
            gradientClip: 1);

        Assert.True(TorchTransformerLanguageModel.MaxAbsDifference(embedding, model.NamedParameters()["token_embedding"]) > 0);
        Assert.True(TorchTransformerLanguageModel.MaxAbsDifference(attention, model.NamedParameters()["layers.0.wq"]) > 0);
        Assert.True(TorchTransformerLanguageModel.MaxAbsDifference(feedForward, model.NamedParameters()["layers.0.w_gate"]) > 0);
    }

    [Fact]
    public void Forward_ReturnsBatchSequenceVocabularyLogits()
    {
        using var model = CreateModel(vocabularySize: 19);

        var logits = model.Forward(model.TensorFrom(new long[,] { { 1, 2, 3 }, { 3, 2, 1 } }));

        Assert.Equal([2, 3, 19], logits.shape);
    }

    [Fact]
    public void Loss_IsCausallyIsolatedFromFutureTokens()
    {
        using var model = CreateModel();
        var targets = model.TensorFrom(new long[,] { { 7, -100, -100, -100 } });

        var left = model.Loss(model.TensorFrom(new long[,] { { 1, 2, 3, 4 } }), targets).ToDouble();
        var right = model.Loss(model.TensorFrom(new long[,] { { 1, 9, 9, 9 } }), targets).ToDouble();

        Assert.Equal(left, right, precision: 6);
    }

    [Fact]
    public void Loss_IgnoresPaddingTargets()
    {
        using var model = CreateModel();
        var targets = model.TensorFrom(new long[,] { { 2, 3, -100, -100 } });

        var left = model.Loss(model.TensorFrom(new long[,] { { 1, 2, 3, 4 } }), targets).ToDouble();
        var right = model.Loss(model.TensorFrom(new long[,] { { 1, 2, 8, 9 } }), targets).ToDouble();

        Assert.Equal(left, right, precision: 6);
    }

    [Fact]
    public void Forward_DropoutDisabledIsDeterministic()
    {
        using var model = CreateModel();
        var input = model.TensorFrom(new long[,] { { 1, 2, 3, 4 } });

        var first = model.Forward(input);
        var second = model.Forward(input);

        Assert.Equal(0, TorchTransformerLanguageModel.MaxAbsDifference(first, second), precision: 7);
    }

    [Fact]
    public void TrainBatch_DeterministicFixtureReducesLoss()
    {
        using var model = CreateModel();
        var inputs = model.TensorFrom(new long[,] { { 1, 2, 3, 4 }, { 1, 2, 3, 4 } });
        var targets = model.TensorFrom(new long[,] { { 2, 3, 4, 5 }, { 2, 3, 4, 5 } });

        var before = model.Loss(inputs, targets).ToDouble();
        for (var step = 0; step < 8; step++)
        {
            model.TrainBatch(inputs, targets, learningRate: 0.01, weightDecay: 0, gradientClip: 1);
        }

        var after = model.Loss(inputs, targets).ToDouble();
        Assert.True(after < before, $"Expected loss to drop below {before}, got {after}.");
    }

    [Fact]
    public void Profiles_ReportExactSmokeParameterCount()
    {
        var profile = TorchTransformerProfiles.SmokeCpu(vocabularySize: 32);
        using var model = new TorchTransformerLanguageModel(profile.Config);

        Assert.Equal(profile.ParameterCount, model.ParameterCount);
        Assert.Equal("smoke-cpu", profile.Name);
        Assert.True(profile.Config.TieOutputEmbeddings);
    }

    [Fact]
    public void TiedEmbeddings_RemoveSeparateLmHeadParameter()
    {
        using var model = new TorchTransformerLanguageModel(new TorchTransformerConfig(
            VocabularySize: 16,
            ContextLength: 16,
            LayerCount: 1,
            Width: 32,
            HeadCount: 4,
            FeedForwardSize: 64,
            Dropout: 0,
            Seed: 42,
            TieOutputEmbeddings: true));

        Assert.DoesNotContain("lm_head", model.NamedParameters().Keys);
        Assert.True(model.ParameterCount < CreateModel().ParameterCount);
    }

    [Fact]
    public void NextTokenLogits_ReturnsVocabularyVector()
    {
        using var model = CreateModel(vocabularySize: 21);

        var logits = model.NextTokenLogits([1, 2, 3]);

        Assert.Equal(21, logits.Length);
        Assert.All(logits, value => Assert.True(float.IsFinite(value)));
    }

    [Fact]
    public async Task Checkpoint_RoundTripPreservesLogitsAndOptimizerStep()
    {
        using var model = CreateModel();
        model.TrainBatch(
            new long[,] { { 1, 2, 3, 4 } },
            new long[,] { { 2, 3, 4, 5 } },
            learningRate: 0.01,
            weightDecay: 0,
            gradientClip: 1);
        var before = model.NextTokenLogits([1, 2, 3, 4]);
        var path = Path.Combine(Path.GetTempPath(), "thoth-tests", Guid.NewGuid().ToString("N"), "torch-transformer.bin");

        await TorchTransformerCheckpoint.SaveAsync(path, model);
        using var loaded = await TorchTransformerCheckpoint.LoadAsync(path);
        var after = loaded.NextTokenLogits([1, 2, 3, 4]);

        Assert.Equal(model.OptimizerStep, loaded.OptimizerStep);
        Assert.Equal(before.Length, after.Length);
        for (var index = 0; index < before.Length; index++)
        {
            Assert.Equal(before[index], after[index], precision: 6);
        }
    }

    private static TorchTransformerLanguageModel CreateModel(int vocabularySize = 16) =>
        new(new TorchTransformerConfig(
            VocabularySize: vocabularySize,
            ContextLength: 16,
            LayerCount: 2,
            Width: 32,
            HeadCount: 4,
            FeedForwardSize: 64,
            Dropout: 0,
            Seed: 42));
}
