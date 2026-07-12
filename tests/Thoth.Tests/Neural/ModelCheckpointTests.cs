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

    [Fact]
    public async Task QualityGate_UnqualifiedWithoutEvaluationMetrics()
    {
        var tokenizer = new ByteTokenizer();
        var model = new RecurrentLanguageModel(new NeuralModelConfig(
            tokenizer.VocabularySize,
            EmbeddingSize: 8,
            HiddenSize: 12,
            SequenceLength: 8,
            Seed: 20));
        var tokens = tokenizer.Encode("quality-check").ToArray();
        model.TrainSequence(tokens[..8], tokens[1..9], 0.005);
        var path = Path.Combine(Path.GetTempPath(), "thoth-tests", Guid.NewGuid().ToString("N"), "model.bin");

        await ModelCheckpoint.SaveAsync(path, model);
        var inspection = await ModelCheckpointQualityGate.InspectAsync(path);

        Assert.Equal(ModelCheckpointStatus.Unqualified, inspection.Status);
        Assert.Contains(inspection.Reasons, reason => reason.Contains("evaluation metrics", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(ModelCheckpointQualityGate.MetadataPath(path)));
    }

    [Fact]
    public async Task QualityGate_QualifiesOnlyRolesWithPassingMetrics()
    {
        var tokenizer = new ByteTokenizer();
        var model = new RecurrentLanguageModel(new NeuralModelConfig(
            tokenizer.VocabularySize,
            EmbeddingSize: 8,
            HiddenSize: 12,
            SequenceLength: 8,
            Seed: 21));
        var tokens = tokenizer.Encode("quality-check").ToArray();
        model.TrainSequence(tokens[..8], tokens[1..9], 0.005);
        var path = Path.Combine(Path.GetTempPath(), "thoth-tests", Guid.NewGuid().ToString("N"), "qualified.bin");
        await ModelCheckpoint.SaveAsync(path, model);

        await ModelCheckpointQualityGate.SaveMetadataAsync(
            path,
            ModelCheckpointMetadata.CreateUnqualified(
                model,
                metrics: new CheckpointEvaluationMetrics(
                    128,
                    4,
                    1.5,
                    4.5,
                    new Dictionary<string, double>
                    {
                        ["generation_health"] = 1,
                        ["no_internal_leak"] = 1
                    })));

        var generation = await ModelCheckpointQualityGate.InspectAsync(path);
        Assert.Equal(ModelCheckpointStatus.QualifiedForGeneration, generation.Status);
        Assert.True(generation.CanUse(ModelRole.Generation));
        Assert.False(generation.CanUse(ModelRole.AgentDecision));

        await ModelCheckpointQualityGate.SaveMetadataAsync(
            path,
            ModelCheckpointMetadata.CreateUnqualified(
                model,
                metrics: new CheckpointEvaluationMetrics(
                    128,
                    4,
                    1.5,
                    4.5,
                    new Dictionary<string, double>
                    {
                        ["generation_health"] = 1,
                        ["no_internal_leak"] = 1,
                        ["language_detection"] = 1,
                        ["tool_routing"] = 1,
                        ["structured_agent_decision"] = 1
                    })));

        var agent = await ModelCheckpointQualityGate.InspectAsync(path);
        Assert.Equal(ModelCheckpointStatus.QualifiedForAgentDecisions, agent.Status);
        Assert.True(agent.CanUse(ModelRole.AgentDecision));
    }
}
