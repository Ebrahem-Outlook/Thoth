using System.Diagnostics;
using System.Text.Json;
using Thoth.Model;
using Thoth.Training.TokenShards;

namespace Thoth.Training.Torch;

public sealed class TorchTransformerTrainer(TorchTransformerLanguageModel model)
{
    public async Task<TorchTrainingReport> TrainAsync(
        IEnumerable<TokenWindow> windows,
        TorchTrainingOptions options,
        string runDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        options.Validate();

        var fullRunDirectory = Path.GetFullPath(runDirectory);
        Directory.CreateDirectory(fullRunDirectory);
        var logPath = Path.Combine(fullRunDirectory, "train.jsonl");
        var startingStep = model.OptimizerStep;
        var stopwatch = Stopwatch.StartNew();
        var initialLoss = double.NaN;
        var finalLoss = double.NaN;
        var microStep = 0;
        var tokensSeen = 0L;
        model.BeginGradientAccumulation();

        foreach (var window in windows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (model.OptimizerStep - startingStep >= options.MaxOptimizerSteps)
            {
                break;
            }

            using var inputs = model.TensorFrom(ToBatch(window.Inputs));
            using var targets = model.TensorFrom(ToBatch(window.Targets));
            var loss = model.AccumulateGradients(inputs, targets);
            microStep++;
            tokensSeen += window.Inputs.Length;
            initialLoss = double.IsNaN(initialLoss) ? loss : initialLoss;
            finalLoss = loss;

            if (microStep % options.GradientAccumulationSteps != 0)
            {
                continue;
            }

            var scheduleStep = (int)(model.OptimizerStep - startingStep + 1);
            var learningRate = LearningRate(
                scheduleStep,
                options.MaxOptimizerSteps,
                options.WarmupSteps,
                options.LearningRate,
                options.MinimumLearningRate);
            model.ApplyGradients(learningRate, options.WeightDecay, options.GradientClip);
            await AppendLogAsync(logPath, new
            {
                step = model.OptimizerStep,
                microStep,
                loss,
                learningRate,
                elapsedSeconds = stopwatch.Elapsed.TotalSeconds,
                tokensPerSecond = tokensSeen / Math.Max(stopwatch.Elapsed.TotalSeconds, 1e-6),
                managedMemoryBytes = GC.GetTotalMemory(forceFullCollection: false)
            }, cancellationToken);

            if (options.CheckpointEverySteps > 0 && model.OptimizerStep % options.CheckpointEverySteps == 0)
            {
                await TorchCheckpointDirectory.SaveAsync(fullRunDirectory, model, options, cancellationToken);
            }

            model.BeginGradientAccumulation();
        }

        if (model.OptimizerStep == startingStep && microStep > 0)
        {
            model.ApplyGradients(options.LearningRate, options.WeightDecay, options.GradientClip);
        }

        await TorchCheckpointDirectory.SaveAsync(fullRunDirectory, model, options, cancellationToken);
        stopwatch.Stop();

        return new TorchTrainingReport(
            startingStep,
            model.OptimizerStep,
            microStep,
            initialLoss,
            finalLoss,
            tokensSeen / Math.Max(stopwatch.Elapsed.TotalSeconds, 1e-6),
            stopwatch.Elapsed,
            fullRunDirectory);
    }

    private static long[,] ToBatch(IReadOnlyList<int> tokens)
    {
        var values = new long[1, tokens.Count];
        for (var index = 0; index < tokens.Count; index++)
        {
            values[0, index] = tokens[index];
        }

        return values;
    }

    private static double LearningRate(int step, int totalSteps, int warmupSteps, double maximum, double minimum)
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

    private static async Task AppendLogAsync(
        string path,
        object value,
        CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + Environment.NewLine;
        await File.AppendAllTextAsync(path, line, cancellationToken);
    }
}
