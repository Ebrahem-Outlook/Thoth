namespace Thoth.Training;

public sealed record TrainingProgress(
    int Epoch,
    int Epochs,
    int StepInEpoch,
    int StepsPerEpoch,
    long GlobalStep,
    double Loss,
    double SmoothedLoss,
    double LearningRate,
    TimeSpan Elapsed);

public sealed record TrainingReport(
    long StartingStep,
    long CompletedStep,
    int Epochs,
    int TokensSeen,
    double InitialLoss,
    double FinalLoss,
    string CheckpointPath,
    TimeSpan Elapsed);
