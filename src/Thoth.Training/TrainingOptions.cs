namespace Thoth.Training;

public sealed record TrainingOptions
{
    public int Epochs { get; init; } = 3;

    public int? StepsPerEpoch { get; init; }

    public int SequenceLength { get; init; } = 128;

    public int BatchSize { get; init; } = 1;

    public int GradientAccumulationSteps { get; init; } = 1;

    public double LearningRate { get; init; } = 0.001;

    public double MinimumLearningRate { get; init; } = 0.00005;

    public int WarmupSteps { get; init; } = 100;

    public double WeightDecay { get; init; } = 0.01;

    public double GradientClip { get; init; } = 1.0;

    public int CheckpointEverySteps { get; init; } = 500;

    public int Seed { get; init; } = 1337;

    public void Validate(int modelSequenceLength)
    {
        if (Epochs < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Epochs));
        }

        if (StepsPerEpoch is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(StepsPerEpoch));
        }

        if (SequenceLength < 2 || SequenceLength > modelSequenceLength)
        {
            throw new ArgumentOutOfRangeException(nameof(SequenceLength));
        }

        if (BatchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(BatchSize));
        }

        if (GradientAccumulationSteps < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(GradientAccumulationSteps));
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
