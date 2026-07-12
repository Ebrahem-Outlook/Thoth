namespace Thoth.Training.Curriculum;

public sealed record CurriculumApprovalReport(
    string StageId,
    string StageName,
    bool RequiresExplicitApproval,
    IReadOnlyList<string> RequiredFacts,
    IReadOnlyList<string> Reasons);

public static class CurriculumApprovalGate
{
    public static CurriculumApprovalReport Inspect(
        TrainingCurriculumStage stage,
        bool yesRunLongTraining,
        TimeSpan estimatedDuration)
    {
        ArgumentNullException.ThrowIfNull(stage);
        var reasons = new List<string>();
        if (stage.RequiresExplicitApproval && !yesRunLongTraining)
        {
            reasons.Add("stage requires explicit user approval");
        }

        if (estimatedDuration > TimeSpan.FromHours(2) && !yesRunLongTraining)
        {
            reasons.Add("estimated duration exceeds two hours");
        }

        return new CurriculumApprovalReport(
            stage.Id,
            stage.Name,
            reasons.Count > 0,
            [
                "parameter count",
                "trainable parameter count",
                "tokenizer vocabulary size",
                "context length",
                "train/validation token counts",
                "device and dtype",
                "CPU thread count",
                "batch size and gradient accumulation",
                "estimated RAM use",
                "estimated checkpoint size",
                "measured tokens/second",
                "estimated duration",
                "checkpoint interval",
                "stop/cancel command",
                "resume command"
            ],
            reasons);
    }
}
