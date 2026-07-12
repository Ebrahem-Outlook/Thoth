using Thoth.Training.Curriculum;

namespace Thoth.Tests.Training;

public sealed class TrainingCurriculumTests
{
    [Fact]
    public void Curriculum_DefinesRequiredStagesInOrder()
    {
        Assert.Equal(["A", "B", "C", "D", "E"], TrainingCurriculum.Stages.Select(stage => stage.Id).ToArray());
        Assert.False(TrainingCurriculum.Resolve("A").RequiresExplicitApproval);
        Assert.True(TrainingCurriculum.Resolve("E").RequiresExplicitApproval);
    }

    [Fact]
    public void ApprovalGate_BlocksLongOrExplicitStagesWithoutFlag()
    {
        var stage = TrainingCurriculum.Resolve("C");

        var blocked = CurriculumApprovalGate.Inspect(stage, yesRunLongTraining: false, TimeSpan.FromHours(3));
        var approved = CurriculumApprovalGate.Inspect(stage, yesRunLongTraining: true, TimeSpan.FromHours(3));

        Assert.True(blocked.RequiresExplicitApproval);
        Assert.Contains(blocked.Reasons, reason => reason.Contains("approval", StringComparison.OrdinalIgnoreCase));
        Assert.False(approved.RequiresExplicitApproval);
        Assert.Contains("measured tokens/second", blocked.RequiredFacts);
    }
}
