namespace Thoth.Model;

public sealed record RecurrentModelState(
    NeuralModelConfig Config,
    long OptimizerStep,
    double[] Embeddings,
    double[] InputWeights,
    double[] RecurrentWeights,
    double[] HiddenBias,
    double[] OutputWeights,
    double[] OutputBias,
    double[] EmbeddingsFirstMoment,
    double[] EmbeddingsSecondMoment,
    double[] InputWeightsFirstMoment,
    double[] InputWeightsSecondMoment,
    double[] RecurrentWeightsFirstMoment,
    double[] RecurrentWeightsSecondMoment,
    double[] HiddenBiasFirstMoment,
    double[] HiddenBiasSecondMoment,
    double[] OutputWeightsFirstMoment,
    double[] OutputWeightsSecondMoment,
    double[] OutputBiasFirstMoment,
    double[] OutputBiasSecondMoment);
