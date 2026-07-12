namespace Thoth.Model;

public sealed class LegacyRecurrentLanguageModel(RecurrentLanguageModel inner) : ITrainableLanguageModel
{
    public RecurrentLanguageModel Inner { get; } = inner ?? throw new ArgumentNullException(nameof(inner));

    public ModelArchitecture Architecture => ModelArchitecture.LegacyRecurrent;

    public int VocabularySize => Inner.Config.VocabularySize;

    public int ContextLength => Inner.Config.SequenceLength;

    public long OptimizerStep => Inner.OptimizerStep;

    public ModelForwardResult Forward(int[,] inputTokenIds, int[,]? targetTokenIds = null)
    {
        ValidateRank(inputTokenIds, targetTokenIds);
        var batch = inputTokenIds.GetLength(0);
        var sequence = inputTokenIds.GetLength(1);
        var logits = new double[batch, sequence, VocabularySize];
        var loss = 0.0;
        var hasTargets = targetTokenIds is not null;

        for (var row = 0; row < batch; row++)
        {
            var hidden = Inner.CreateHiddenState();
            var inputs = new int[sequence];
            var targets = hasTargets ? new int[sequence] : [];

            for (var column = 0; column < sequence; column++)
            {
                var token = inputTokenIds[row, column];
                inputs[column] = token;
                var rowLogits = Inner.ForwardToken(token, hidden);
                for (var vocab = 0; vocab < VocabularySize; vocab++)
                {
                    logits[row, column, vocab] = rowLogits[vocab];
                }

                if (hasTargets)
                {
                    targets[column] = targetTokenIds![row, column];
                }
            }

            if (hasTargets)
            {
                loss += Inner.EvaluateSequence(inputs, targets);
            }
        }

        return new ModelForwardResult(logits, hasTargets ? loss / batch : null);
    }

    public double EvaluateBatch(int[,] inputTokenIds, int[,] targetTokenIds) =>
        Forward(inputTokenIds, targetTokenIds).Loss ?? double.NaN;

    public double TrainBatch(
        int[,] inputTokenIds,
        int[,] targetTokenIds,
        double learningRate,
        double weightDecay = 0.01,
        double gradientClip = 1.0)
    {
        ValidateRank(inputTokenIds, targetTokenIds);
        var batch = inputTokenIds.GetLength(0);
        var sequence = inputTokenIds.GetLength(1);
        var loss = 0.0;

        for (var row = 0; row < batch; row++)
        {
            var inputs = new int[sequence];
            var targets = new int[sequence];
            for (var column = 0; column < sequence; column++)
            {
                inputs[column] = inputTokenIds[row, column];
                targets[column] = targetTokenIds[row, column];
            }

            loss += Inner.TrainSequence(inputs, targets, learningRate, weightDecay, gradientClip);
        }

        return loss / batch;
    }

    public ModelGenerationState CreateGenerationState(IEnumerable<int> promptTokens) =>
        new(promptTokens.TakeLast(ContextLength).ToArray(), ContextLength);

    private static void ValidateRank(int[,] inputs, int[,]? targets)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.GetLength(0) < 1 || inputs.GetLength(1) < 1)
        {
            throw new ArgumentException("Input batch must be non-empty.", nameof(inputs));
        }

        if (targets is not null &&
            (targets.GetLength(0) != inputs.GetLength(0) ||
             targets.GetLength(1) != inputs.GetLength(1)))
        {
            throw new ArgumentException("Target batch shape must match input batch shape.", nameof(targets));
        }
    }
}

