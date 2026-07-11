using Thoth.Model;
using Thoth.Model.Persistence;
using Thoth.Tokenization;

namespace Thoth.Tests.Neural;

public sealed class ModelCheckpointTests
{
    [Fact]
    public async Task SaveLoad_PreservesWeightsAndOptimizerStep()
    {
        var tokenizer = new ByteTokenizer();
        var model = new RecurrentLanguageModel(new NeuralModelConfig(
            tokenizer.VocabularySize,
            EmbeddingSize: 8,
            HiddenSize: 12,
            SequenceLength: 8,
            Seed: 19));
        var tokens = tokenizer.Encode("checkpoint").ToArray();
        model.TrainSequence(tokens[..8], tokens[1..9], 0.005);

        var hiddenBefore = model.CreateHiddenState();
        var logitsBefore = model.ForwardToken(tokens[0], hiddenBefore);
        var path = Path.Combine(Path.GetTempPath(), "thoth-tests", Guid.NewGuid().ToString("N"), "model.bin");
        await ModelCheckpoint.SaveAsync(path, model);
        var loaded = await ModelCheckpoint.LoadAsync(path);
        var hiddenAfter = loaded.CreateHiddenState();
        var logitsAfter = loaded.ForwardToken(tokens[0], hiddenAfter);

        Assert.Equal(model.OptimizerStep, loaded.OptimizerStep);
        Assert.Equal(logitsBefore.Length, logitsAfter.Length);
        for (var index = 0; index < logitsBefore.Length; index++)
        {
            Assert.Equal(logitsBefore[index], logitsAfter[index], precision: 12);
        }
    }
}
