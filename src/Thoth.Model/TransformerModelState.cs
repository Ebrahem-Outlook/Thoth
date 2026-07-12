namespace Thoth.Model;

public sealed record TransformerModelState(
    TransformerConfig Config,
    long OptimizerStep,
    double[] TokenEmbeddings,
    double[] FinalNormWeight,
    double[] LmHead,
    double[] OutputBias,
    double[] LmHeadFirstMoment,
    double[] LmHeadSecondMoment,
    double[] OutputBiasFirstMoment,
    double[] OutputBiasSecondMoment,
    IReadOnlyList<TransformerLayerState> Layers);

public sealed record TransformerLayerState(
    double[] AttentionNormWeight,
    double[] FeedForwardNormWeight,
    double[] QueryWeight,
    double[] KeyWeight,
    double[] ValueWeight,
    double[] OutputWeight,
    double[] GateWeight,
    double[] UpWeight,
    double[] DownWeight);

