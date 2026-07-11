using Thoth.Model;
using Thoth.Tokenization;

namespace Thoth.Tests.Neural;

public sealed class RecurrentLanguageModelTests
{
    [Fact]
    public void TrainSequence_ReducesLossOnRepeatedPattern()
    {
        var tokenizer = new ByteTokenizer();
        var model = new RecurrentLanguageModel(new NeuralModelConfig(
            tokenizer.VocabularySize,
            EmbeddingSize: 8,
            HiddenSize: 16,
            SequenceLength: 16,
            Seed: 7));
        var tokens = tokenizer.Encode("abcabcabcabcabcab").ToArray();
        var inputs = tokens[..16];
        var targets = tokens[1..17];
        var initial = model.EvaluateSequence(inputs, targets);

        for (var step = 0; step < 250; step++)
        {
            model.TrainSequence(inputs, targets, learningRate: 0.01, weightDecay: 0, gradientClip: 5);
        }

        var final = model.EvaluateSequence(inputs, targets);
        Assert.True(final < initial * 0.65, $"Expected loss to decrease. Initial={initial}, Final={final}");
    }
}
