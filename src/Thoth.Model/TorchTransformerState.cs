namespace Thoth.Model;

public sealed record TorchTransformerState(
    TorchTransformerConfig Config,
    long OptimizerStep,
    IReadOnlyList<TorchParameterSnapshot> Parameters);

public sealed record TorchParameterSnapshot(
    string Name,
    long[] Shape,
    float[] Value,
    float[] FirstMoment,
    float[] SecondMoment);
