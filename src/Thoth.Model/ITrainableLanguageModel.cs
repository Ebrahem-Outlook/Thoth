namespace Thoth.Model;

public enum ModelArchitecture
{
    LegacyRecurrent,
    DecoderOnlyTransformer
}

public sealed record ModelForwardResult(double[,,] Logits, double? Loss = null)
{
    public int BatchSize => Logits.GetLength(0);

    public int SequenceLength => Logits.GetLength(1);

    public int VocabularySize => Logits.GetLength(2);
}

public sealed record ModelGenerationState(IReadOnlyList<int> Tokens, int ContextLength);

public interface ITrainableLanguageModel
{
    ModelArchitecture Architecture { get; }

    int VocabularySize { get; }

    int ContextLength { get; }

    long OptimizerStep { get; }

    ModelForwardResult Forward(int[,] inputTokenIds, int[,]? targetTokenIds = null);

    double EvaluateBatch(int[,] inputTokenIds, int[,] targetTokenIds);

    double TrainBatch(
        int[,] inputTokenIds,
        int[,] targetTokenIds,
        double learningRate,
        double weightDecay = 0.01,
        double gradientClip = 1.0);

    ModelGenerationState CreateGenerationState(IEnumerable<int> promptTokens);
}

