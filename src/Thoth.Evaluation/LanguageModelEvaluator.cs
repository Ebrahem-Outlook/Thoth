using Thoth.Model;

namespace Thoth.Evaluation;

public sealed record EvaluationReport(
    int EvaluatedTokens,
    int EvaluatedSequences,
    double AverageLoss,
    double Perplexity,
    IReadOnlyDictionary<string, double>? Scores = null);

public static class LanguageModelEvaluator
{
    public static EvaluationReport Evaluate(
        RecurrentLanguageModel model,
        IReadOnlyList<int> tokens,
        int? sequenceLength = null,
        int maximumSequences = 1000)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(tokens);

        var length = Math.Min(sequenceLength ?? model.Config.SequenceLength, model.Config.SequenceLength);
        if (length < 2 || tokens.Count <= length)
        {
            throw new ArgumentException("Not enough tokens for evaluation.", nameof(tokens));
        }

        var totalLoss = 0.0;
        var sequences = 0;
        var evaluatedTokens = 0;
        for (var start = 0; start + length < tokens.Count && sequences < maximumSequences; start += length)
        {
            var inputs = new int[length];
            var targets = new int[length];
            for (var index = 0; index < length; index++)
            {
                inputs[index] = tokens[start + index];
                targets[index] = tokens[start + index + 1];
            }

            totalLoss += model.EvaluateSequence(inputs, targets) * length;
            evaluatedTokens += length;
            sequences++;
        }

        var averageLoss = totalLoss / Math.Max(evaluatedTokens, 1);
        var perplexity = Math.Exp(Math.Min(averageLoss, 20));
        return new EvaluationReport(
            evaluatedTokens,
            sequences,
            averageLoss,
            perplexity,
            new Dictionary<string, double>
            {
                ["loss_health"] = ComputeLossHealth(averageLoss, perplexity),
                ["finite_loss"] = double.IsFinite(averageLoss) ? 1.0 : 0.0
            });
    }

    public static EvaluationReport Evaluate(
        TransformerLanguageModel model,
        IReadOnlyList<int> tokens,
        int? sequenceLength = null,
        int maximumSequences = 1000)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(tokens);

        var length = Math.Min(sequenceLength ?? Math.Min(128, model.ContextLength), model.ContextLength);
        if (length < 2 || tokens.Count <= length)
        {
            throw new ArgumentException("Not enough tokens for evaluation.", nameof(tokens));
        }

        var totalLoss = 0.0;
        var sequences = 0;
        var evaluatedTokens = 0;
        for (var start = 0; start + length < tokens.Count && sequences < maximumSequences; start += length)
        {
            var inputs = new int[1, length];
            var targets = new int[1, length];
            for (var index = 0; index < length; index++)
            {
                inputs[0, index] = tokens[start + index];
                targets[0, index] = tokens[start + index + 1];
            }

            totalLoss += model.EvaluateBatch(inputs, targets) * length;
            evaluatedTokens += length;
            sequences++;
        }

        var averageLoss = totalLoss / Math.Max(evaluatedTokens, 1);
        var perplexity = Math.Exp(Math.Min(averageLoss, 20));
        return new EvaluationReport(
            evaluatedTokens,
            sequences,
            averageLoss,
            perplexity,
            new Dictionary<string, double>
            {
                ["loss_health"] = ComputeLossHealth(averageLoss, perplexity),
                ["finite_loss"] = double.IsFinite(averageLoss) ? 1.0 : 0.0
            });
    }

    private static double ComputeLossHealth(double averageLoss, double perplexity)
    {
        if (!double.IsFinite(averageLoss) || !double.IsFinite(perplexity))
        {
            return 0.0;
        }

        var lossScore = 1.0 / (1.0 + Math.Max(averageLoss, 0.0));
        var perplexityScore = 1.0 / (1.0 + Math.Log10(Math.Max(perplexity, 1.0)));
        return Math.Clamp((lossScore + perplexityScore) / 2.0, 0.0, 1.0);
    }
}
