using System.Diagnostics;
using Thoth.Model;
using Thoth.Model.Persistence;

namespace Thoth.Training;

public sealed class TransformerLanguageModelTrainer(TransformerLanguageModel model)
{
    public async Task<TrainingReport> TrainAsync(
        IReadOnlyList<int> corpusTokens,
        TrainingOptions options,
        string checkpointPath,
        string tokenizer = ModelCheckpointMetadata.BpeTokenizer,
        IProgress<TrainingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(corpusTokens);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointPath);
        options.Validate(model.ContextLength);

        if (corpusTokens.Count <= options.SequenceLength)
        {
            throw new InvalidDataException(
                $"Corpus has {corpusTokens.Count} tokens but training needs more than {options.SequenceLength}.");
        }

        var stepsPerEpoch = options.StepsPerEpoch ?? Math.Max(1, (corpusTokens.Count - 1) / (options.SequenceLength * options.BatchSize));
        var totalSteps = checked(stepsPerEpoch * options.Epochs);
        var random = new Random(options.Seed + unchecked((int)model.OptimizerStep));
        var stopwatch = Stopwatch.StartNew();
        var startingStep = model.OptimizerStep;
        var initialLoss = double.NaN;
        var finalLoss = double.NaN;
        var smoothedLoss = double.NaN;
        var tokensSeen = 0;

        for (var epoch = 1; epoch <= options.Epochs; epoch++)
        {
            for (var stepInEpoch = 1; stepInEpoch <= stepsPerEpoch; stepInEpoch++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (inputs, targets) = CreateBatch(corpusTokens, options.SequenceLength, options.BatchSize, random);
                var scheduleStep = checked((epoch - 1) * stepsPerEpoch + stepInEpoch);
                var learningRate = GetLearningRate(
                    scheduleStep,
                    totalSteps,
                    options.WarmupSteps,
                    options.LearningRate,
                    options.MinimumLearningRate);

                var loss = model.TrainBatch(
                    inputs,
                    targets,
                    learningRate,
                    options.WeightDecay,
                    options.GradientClip);

                if (!double.IsFinite(loss))
                {
                    throw new InvalidOperationException("Transformer training diverged: loss became non-finite.");
                }

                initialLoss = double.IsNaN(initialLoss) ? loss : initialLoss;
                finalLoss = loss;
                smoothedLoss = double.IsNaN(smoothedLoss) ? loss : 0.97 * smoothedLoss + 0.03 * loss;
                tokensSeen += options.SequenceLength * options.BatchSize;

                progress?.Report(new TrainingProgress(
                    epoch,
                    options.Epochs,
                    stepInEpoch,
                    stepsPerEpoch,
                    model.OptimizerStep,
                    loss,
                    smoothedLoss,
                    learningRate,
                    stopwatch.Elapsed));

                if (options.CheckpointEverySteps > 0 &&
                    model.OptimizerStep % options.CheckpointEverySteps == 0)
                {
                    await TransformerCheckpoint.SaveAsync(
                        checkpointPath,
                        model,
                        includeOptimizer: true,
                        tokenizer: tokenizer,
                        cancellationToken: cancellationToken);
                }
            }
        }

        await TransformerCheckpoint.SaveAsync(
            checkpointPath,
            model,
            includeOptimizer: true,
            tokenizer: tokenizer,
            cancellationToken: cancellationToken);
        stopwatch.Stop();

        return new TrainingReport(
            startingStep,
            model.OptimizerStep,
            options.Epochs,
            tokensSeen,
            initialLoss,
            finalLoss,
            Path.GetFullPath(checkpointPath),
            stopwatch.Elapsed);
    }

    public static (int[,] Inputs, int[,] Targets) CreateBatch(
        IReadOnlyList<int> corpusTokens,
        int sequenceLength,
        int batchSize,
        Random random)
    {
        if (corpusTokens.Count <= sequenceLength)
        {
            throw new InvalidDataException("Corpus is shorter than the requested sequence length.");
        }

        var inputs = new int[batchSize, sequenceLength];
        var targets = new int[batchSize, sequenceLength];
        var maximumStart = corpusTokens.Count - sequenceLength - 1;
        for (var row = 0; row < batchSize; row++)
        {
            var start = maximumStart == 0 ? 0 : random.Next(maximumStart + 1);
            for (var index = 0; index < sequenceLength; index++)
            {
                inputs[row, index] = corpusTokens[start + index];
                targets[row, index] = corpusTokens[start + index + 1];
            }
        }

        return (inputs, targets);
    }

    private static double GetLearningRate(
        int step,
        int totalSteps,
        int warmupSteps,
        double maximum,
        double minimum)
    {
        if (warmupSteps > 0 && step <= warmupSteps)
        {
            return maximum * step / warmupSteps;
        }

        var decaySteps = Math.Max(totalSteps - warmupSteps, 1);
        var progress = Math.Clamp((step - warmupSteps) / (double)decaySteps, 0, 1);
        var cosine = 0.5 * (1.0 + Math.Cos(Math.PI * progress));
        return minimum + (maximum - minimum) * cosine;
    }
}
