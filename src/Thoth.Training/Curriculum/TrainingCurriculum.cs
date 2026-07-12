namespace Thoth.Training.Curriculum;

public sealed record TrainingCurriculumStage(
    string Id,
    string Name,
    string ModelProfile,
    string DataTarget,
    string Purpose,
    bool RequiresExplicitApproval,
    IReadOnlyList<string> PassCriteria);

public static class TrainingCurriculum
{
    public static IReadOnlyList<TrainingCurriculumStage> Stages { get; } =
    [
        new(
            "A",
            "Mathematical smoke test",
            "smoke-cpu",
            "tiny deterministic fixture",
            "Overfit one tiny batch and prove loss decreases, checkpointing works, and generation changes from random baseline.",
            RequiresExplicitApproval: false,
            PassCriteria:
            [
                "loss decreases substantially",
                "no NaN/Inf",
                "all parameter groups update",
                "save/load/resume works",
                "generated output reflects tiny fixture"
            ]),
        new(
            "B",
            "Pipeline pilot",
            "smoke-cpu or reduced laptop-pilot",
            "1-5M local approved tokens",
            "Validate real corpus, sharding, validation, checkpointing, and generation under a short local run.",
            RequiresExplicitApproval: false,
            PassCriteria:
            [
                "token shard loader works",
                "validation runs",
                "checkpoint retention works",
                "estimated runtime stays under approval limit"
            ]),
        new(
            "C",
            "First real local pretraining",
            "laptop-pilot",
            "10-20M unique curated tokens",
            "Train the first useful local checkpoint on CPU after benchmark-first estimation.",
            RequiresExplicitApproval: true,
            PassCriteria:
            [
                "approval report accepted",
                "quality improves over random baseline",
                "no training instability",
                "checkpoint can generate"
            ]),
        new(
            "D",
            "Supervised instruction tuning",
            "qualified base checkpoint",
            "5,000-20,000 reviewed examples",
            "Teach role-token conversational behavior while deterministic cognition remains responsible for routing.",
            RequiresExplicitApproval: true,
            PassCriteria:
            [
                "assistant-token loss improves",
                "held-out instruction examples remain unseen",
                "no no-internal-leak regressions"
            ]),
        new(
            "E",
            "Laptop max optional",
            "laptop-max-experimental",
            "approved larger local corpus",
            "Only after laptop-pilot proves value and the user accepts likely multi-day local training.",
            RequiresExplicitApproval: true,
            PassCriteria:
            [
                "pilot beats baselines",
                "quality gate works",
                "thermals and throughput acceptable",
                "user explicitly approves"
            ])
    ];

    public static TrainingCurriculumStage Resolve(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Stages.FirstOrDefault(stage => stage.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
               ?? throw new ArgumentOutOfRangeException(nameof(id), $"Unknown training curriculum stage: {id}");
    }
}
