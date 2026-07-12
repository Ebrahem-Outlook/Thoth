namespace Thoth.Training.Torch;

public sealed record TorchTrainingOptions
{
    public int MaxOptimizerSteps { get; init; } = 10;

    public int GradientAccumulationSteps { get; init; } = 1;

    public double LearningRate { get; init; } = 3e-4;

    public double MinimumLearningRate { get; init; } = 3e-5;

    public int WarmupSteps { get; init; } = 100;

    public double WeightDecay { get; init; } = 0.1;

    public double GradientClip { get; init; } = 1.0;

    public int CheckpointEverySteps { get; init; } = 500;

    public int Seed { get; init; } = 1337;

    public string RunId { get; init; } = "local-run";

    public void Validate()
    {
        if (MaxOptimizerSteps < 1 || GradientAccumulationSteps < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxOptimizerSteps));
        }

        if (LearningRate <= 0 || MinimumLearningRate <= 0 || MinimumLearningRate > LearningRate)
        {
            throw new ArgumentOutOfRangeException(nameof(LearningRate));
        }

        if (WarmupSteps < 0 || CheckpointEverySteps < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(WarmupSteps));
        }
    }
}

public sealed record TorchTrainingReport(
    long StartingStep,
    long CompletedStep,
    int MicroSteps,
    double InitialLoss,
    double FinalLoss,
    double TokensPerSecond,
    TimeSpan Elapsed,
    string RunDirectory);
