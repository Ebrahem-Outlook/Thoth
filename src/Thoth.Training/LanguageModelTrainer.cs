using System.Diagnostics;
using Thoth.Model;
using Thoth.Model.Persistence;
using Thoth.Tokenization;

namespace Thoth.Training;

public sealed class LanguageModelTrainer(
    RecurrentLanguageModel model,
    ITextTokenizer tokenizer)
{
    public async Task<TrainingReport> TrainAsync(
        IReadOnlyList<int> corpusTokens,
        TrainingOptions options,
        string checkpointPath,
        IProgress<TrainingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(corpusTokens);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointPath);
        options.Validate(model.Config.SequenceLength);

        if (model.Config.VocabularySize != tokenizer.VocabularySize)
        {
            throw new InvalidOperationException("Tokenizer vocabulary does not match the model vocabulary.");
        }

        if (corpusTokens.Count <= options.SequenceLength)
        {
            throw new InvalidDataException(
                $"Corpus has {corpusTokens.Count} tokens but training needs more than {options.SequenceLength}.");
        }

        var stepsPerEpoch = options.StepsPerEpoch ?? Math.Max(1, (corpusTokens.Count - 1) / options.SequenceLength);
        var totalSteps = checked(stepsPerEpoch * options.Epochs);
        var random = new Random(options.Seed + unchecked((int)model.OptimizerStep));
        var stopwatch = Stopwatch.StartNew();
        var startingStep = model.OptimizerStep;
        var initialLoss = double.NaN;
        var finalLoss = double.NaN;
        var exponentialLoss = double.NaN;
        var tokensSeen = 0;

        for (var epoch = 1; epoch <= options.Epochs; epoch++)
        {
            for (var stepInEpoch = 1; stepInEpoch <= stepsPerEpoch; stepInEpoch++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var maximumStart = corpusTokens.Count - options.SequenceLength - 1;
                var start = maximumStart == 0 ? 0 : random.Next(maximumStart + 1);
                var inputs = new int[options.SequenceLength];
                var targets = new int[options.SequenceLength];

                for (var index = 0; index < options.SequenceLength; index++)
                {
                    inputs[index] = corpusTokens[start + index];
                    targets[index] = corpusTokens[start + index + 1];
                }

                var scheduleStep = checked((epoch - 1) * stepsPerEpoch + stepInEpoch);
                var learningRate = GetLearningRate(
                    scheduleStep,
                    totalSteps,
                    options.WarmupSteps,
                    options.LearningRate,
                    options.MinimumLearningRate);
                var loss = model.TrainSequence(
                    inputs,
                    targets,
                    learningRate,
                    options.WeightDecay,
                    options.GradientClip);

                if (double.IsNaN(loss) || double.IsInfinity(loss))
                {
                    throw new InvalidOperationException("Training diverged: loss became non-finite.");
                }

                initialLoss = double.IsNaN(initialLoss) ? loss : initialLoss;
                finalLoss = loss;
                exponentialLoss = double.IsNaN(exponentialLoss) ? loss : 0.97 * exponentialLoss + 0.03 * loss;
                tokensSeen += options.SequenceLength;

                progress?.Report(new TrainingProgress(
                    epoch,
                    options.Epochs,
                    stepInEpoch,
                    stepsPerEpoch,
                    model.OptimizerStep,
                    loss,
                    exponentialLoss,
                    learningRate,
                    stopwatch.Elapsed));

                if (options.CheckpointEverySteps > 0 &&
                    model.OptimizerStep % options.CheckpointEverySteps == 0)
                {
                    await ModelCheckpoint.SaveAsync(checkpointPath, model, includeOptimizer: true, cancellationToken);
                }
            }
        }

        await ModelCheckpoint.SaveAsync(checkpointPath, model, includeOptimizer: true, cancellationToken);
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
