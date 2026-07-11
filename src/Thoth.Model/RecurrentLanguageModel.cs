namespace Thoth.Model;

/// <summary>
/// A small recurrent neural language model implemented with plain C# arrays.
/// It is deliberately dependency-free: the weights start randomly, gradients
/// are computed with back-propagation through time, and AdamW updates the model.
/// This is Thoth's bootstrap neural core, not a pretrained or wrapped model.
/// </summary>
public sealed class RecurrentLanguageModel
{
    private const double AdamBeta1 = 0.9;
    private const double AdamBeta2 = 0.999;
    private const double AdamEpsilon = 1e-8;

    private readonly double[] embeddings;
    private readonly double[] inputWeights;
    private readonly double[] recurrentWeights;
    private readonly double[] hiddenBias;
    private readonly double[] outputWeights;
    private readonly double[] outputBias;

    private readonly double[] embeddingsFirstMoment;
    private readonly double[] embeddingsSecondMoment;
    private readonly double[] inputWeightsFirstMoment;
    private readonly double[] inputWeightsSecondMoment;
    private readonly double[] recurrentWeightsFirstMoment;
    private readonly double[] recurrentWeightsSecondMoment;
    private readonly double[] hiddenBiasFirstMoment;
    private readonly double[] hiddenBiasSecondMoment;
    private readonly double[] outputWeightsFirstMoment;
    private readonly double[] outputWeightsSecondMoment;
    private readonly double[] outputBiasFirstMoment;
    private readonly double[] outputBiasSecondMoment;

    private long optimizerStep;

    public RecurrentLanguageModel(NeuralModelConfig config)
    {
        config.Validate();
        Config = config;

        embeddings = new double[config.VocabularySize * config.EmbeddingSize];
        inputWeights = new double[config.HiddenSize * config.EmbeddingSize];
        recurrentWeights = new double[config.HiddenSize * config.HiddenSize];
        hiddenBias = new double[config.HiddenSize];
        outputWeights = new double[config.VocabularySize * config.HiddenSize];
        outputBias = new double[config.VocabularySize];

        embeddingsFirstMoment = new double[embeddings.Length];
        embeddingsSecondMoment = new double[embeddings.Length];
        inputWeightsFirstMoment = new double[inputWeights.Length];
        inputWeightsSecondMoment = new double[inputWeights.Length];
        recurrentWeightsFirstMoment = new double[recurrentWeights.Length];
        recurrentWeightsSecondMoment = new double[recurrentWeights.Length];
        hiddenBiasFirstMoment = new double[hiddenBias.Length];
        hiddenBiasSecondMoment = new double[hiddenBias.Length];
        outputWeightsFirstMoment = new double[outputWeights.Length];
        outputWeightsSecondMoment = new double[outputWeights.Length];
        outputBiasFirstMoment = new double[outputBias.Length];
        outputBiasSecondMoment = new double[outputBias.Length];

        InitializeWeights(new Random(config.Seed));
    }

    public NeuralModelConfig Config { get; }

    public long OptimizerStep => optimizerStep;

    public int ParameterCount =>
        embeddings.Length + inputWeights.Length + recurrentWeights.Length +
        hiddenBias.Length + outputWeights.Length + outputBias.Length;

    public double[] CreateHiddenState() => new double[Config.HiddenSize];

    /// <summary>
    /// Advances one token. The supplied hidden state is updated in-place and
    /// the returned logits predict the token following <paramref name="tokenId"/>.
    /// </summary>
    public double[] ForwardToken(int tokenId, double[] hiddenState)
    {
        ValidateToken(tokenId);
        ArgumentNullException.ThrowIfNull(hiddenState);
        if (hiddenState.Length != Config.HiddenSize)
        {
            throw new ArgumentException("Hidden state has the wrong size.", nameof(hiddenState));
        }

        var previous = (double[])hiddenState.Clone();
        var embeddingOffset = tokenId * Config.EmbeddingSize;

        for (var hiddenIndex = 0; hiddenIndex < Config.HiddenSize; hiddenIndex++)
        {
            var activation = hiddenBias[hiddenIndex];
            var inputOffset = hiddenIndex * Config.EmbeddingSize;
            for (var embeddingIndex = 0; embeddingIndex < Config.EmbeddingSize; embeddingIndex++)
            {
                activation += inputWeights[inputOffset + embeddingIndex] * embeddings[embeddingOffset + embeddingIndex];
            }

            var recurrentOffset = hiddenIndex * Config.HiddenSize;
            for (var previousIndex = 0; previousIndex < Config.HiddenSize; previousIndex++)
            {
                activation += recurrentWeights[recurrentOffset + previousIndex] * previous[previousIndex];
            }

            hiddenState[hiddenIndex] = Math.Tanh(activation);
        }

        return ProjectToLogits(hiddenState);
    }

    public double EvaluateSequence(ReadOnlySpan<int> inputs, ReadOnlySpan<int> targets)
    {
        ValidateSequence(inputs, targets);
        var hidden = CreateHiddenState();
        var loss = 0.0;

        for (var index = 0; index < inputs.Length; index++)
        {
            var logits = ForwardToken(inputs[index], hidden);
            var probabilities = Softmax(logits);
            loss -= Math.Log(Math.Max(probabilities[targets[index]], 1e-12));
        }

        return loss / inputs.Length;
    }

    public double TrainSequence(
        ReadOnlySpan<int> inputs,
        ReadOnlySpan<int> targets,
        double learningRate,
        double weightDecay = 0.01,
        double gradientClip = 1.0)
    {
        ValidateSequence(inputs, targets);
        if (learningRate <= 0 || double.IsNaN(learningRate) || double.IsInfinity(learningRate))
        {
            throw new ArgumentOutOfRangeException(nameof(learningRate));
        }

        var steps = inputs.Length;
        var hiddenStates = new double[(steps + 1) * Config.HiddenSize];
        var probabilities = new double[steps * Config.VocabularySize];
        var loss = ForwardTrainingPass(inputs, targets, hiddenStates, probabilities);
        var gradients = new GradientSet(this);
        BackwardTrainingPass(inputs, targets, hiddenStates, probabilities, gradients);
        gradients.Scale(1.0 / steps);
        gradients.ClipByGlobalNorm(Math.Max(gradientClip, 1e-9));

        optimizerStep++;
        ApplyAdamW(embeddings, gradients.Embeddings, embeddingsFirstMoment, embeddingsSecondMoment, learningRate, weightDecay);
        ApplyAdamW(inputWeights, gradients.InputWeights, inputWeightsFirstMoment, inputWeightsSecondMoment, learningRate, weightDecay);
        ApplyAdamW(recurrentWeights, gradients.RecurrentWeights, recurrentWeightsFirstMoment, recurrentWeightsSecondMoment, learningRate, weightDecay);
        ApplyAdamW(hiddenBias, gradients.HiddenBias, hiddenBiasFirstMoment, hiddenBiasSecondMoment, learningRate, 0);
        ApplyAdamW(outputWeights, gradients.OutputWeights, outputWeightsFirstMoment, outputWeightsSecondMoment, learningRate, weightDecay);
        ApplyAdamW(outputBias, gradients.OutputBias, outputBiasFirstMoment, outputBiasSecondMoment, learningRate, 0);

        return loss / steps;
    }

    public RecurrentModelState ExportState(bool includeOptimizer = true) =>
        new(
            Config,
            optimizerStep,
            (double[])embeddings.Clone(),
            (double[])inputWeights.Clone(),
            (double[])recurrentWeights.Clone(),
            (double[])hiddenBias.Clone(),
            (double[])outputWeights.Clone(),
            (double[])outputBias.Clone(),
            includeOptimizer ? (double[])embeddingsFirstMoment.Clone() : new double[embeddings.Length],
            includeOptimizer ? (double[])embeddingsSecondMoment.Clone() : new double[embeddings.Length],
            includeOptimizer ? (double[])inputWeightsFirstMoment.Clone() : new double[inputWeights.Length],
            includeOptimizer ? (double[])inputWeightsSecondMoment.Clone() : new double[inputWeights.Length],
            includeOptimizer ? (double[])recurrentWeightsFirstMoment.Clone() : new double[recurrentWeights.Length],
            includeOptimizer ? (double[])recurrentWeightsSecondMoment.Clone() : new double[recurrentWeights.Length],
            includeOptimizer ? (double[])hiddenBiasFirstMoment.Clone() : new double[hiddenBias.Length],
            includeOptimizer ? (double[])hiddenBiasSecondMoment.Clone() : new double[hiddenBias.Length],
            includeOptimizer ? (double[])outputWeightsFirstMoment.Clone() : new double[outputWeights.Length],
            includeOptimizer ? (double[])outputWeightsSecondMoment.Clone() : new double[outputWeights.Length],
            includeOptimizer ? (double[])outputBiasFirstMoment.Clone() : new double[outputBias.Length],
            includeOptimizer ? (double[])outputBiasSecondMoment.Clone() : new double[outputBias.Length]);

    public static RecurrentLanguageModel FromState(RecurrentModelState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var model = new RecurrentLanguageModel(state.Config);
        model.ImportState(state);
        return model;
    }

    public void ImportState(RecurrentModelState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Config != Config)
        {
            throw new InvalidDataException("Checkpoint model configuration does not match the current model.");
        }

        CopyExact(state.Embeddings, embeddings, nameof(state.Embeddings));
        CopyExact(state.InputWeights, inputWeights, nameof(state.InputWeights));
        CopyExact(state.RecurrentWeights, recurrentWeights, nameof(state.RecurrentWeights));
        CopyExact(state.HiddenBias, hiddenBias, nameof(state.HiddenBias));
        CopyExact(state.OutputWeights, outputWeights, nameof(state.OutputWeights));
        CopyExact(state.OutputBias, outputBias, nameof(state.OutputBias));
        CopyExact(state.EmbeddingsFirstMoment, embeddingsFirstMoment, nameof(state.EmbeddingsFirstMoment));
        CopyExact(state.EmbeddingsSecondMoment, embeddingsSecondMoment, nameof(state.EmbeddingsSecondMoment));
        CopyExact(state.InputWeightsFirstMoment, inputWeightsFirstMoment, nameof(state.InputWeightsFirstMoment));
        CopyExact(state.InputWeightsSecondMoment, inputWeightsSecondMoment, nameof(state.InputWeightsSecondMoment));
        CopyExact(state.RecurrentWeightsFirstMoment, recurrentWeightsFirstMoment, nameof(state.RecurrentWeightsFirstMoment));
        CopyExact(state.RecurrentWeightsSecondMoment, recurrentWeightsSecondMoment, nameof(state.RecurrentWeightsSecondMoment));
        CopyExact(state.HiddenBiasFirstMoment, hiddenBiasFirstMoment, nameof(state.HiddenBiasFirstMoment));
        CopyExact(state.HiddenBiasSecondMoment, hiddenBiasSecondMoment, nameof(state.HiddenBiasSecondMoment));
        CopyExact(state.OutputWeightsFirstMoment, outputWeightsFirstMoment, nameof(state.OutputWeightsFirstMoment));
        CopyExact(state.OutputWeightsSecondMoment, outputWeightsSecondMoment, nameof(state.OutputWeightsSecondMoment));
        CopyExact(state.OutputBiasFirstMoment, outputBiasFirstMoment, nameof(state.OutputBiasFirstMoment));
        CopyExact(state.OutputBiasSecondMoment, outputBiasSecondMoment, nameof(state.OutputBiasSecondMoment));
        optimizerStep = Math.Max(state.OptimizerStep, 0);
    }

    private double ForwardTrainingPass(
        ReadOnlySpan<int> inputs,
        ReadOnlySpan<int> targets,
        double[] hiddenStates,
        double[] probabilities)
    {
        var loss = 0.0;
        var logits = new double[Config.VocabularySize];

        for (var step = 0; step < inputs.Length; step++)
        {
            var previousHiddenOffset = step * Config.HiddenSize;
            var currentHiddenOffset = (step + 1) * Config.HiddenSize;
            var embeddingOffset = inputs[step] * Config.EmbeddingSize;

            for (var hiddenIndex = 0; hiddenIndex < Config.HiddenSize; hiddenIndex++)
            {
                var activation = hiddenBias[hiddenIndex];
                var inputOffset = hiddenIndex * Config.EmbeddingSize;
                for (var embeddingIndex = 0; embeddingIndex < Config.EmbeddingSize; embeddingIndex++)
                {
                    activation += inputWeights[inputOffset + embeddingIndex] * embeddings[embeddingOffset + embeddingIndex];
                }

                var recurrentOffset = hiddenIndex * Config.HiddenSize;
                for (var previousIndex = 0; previousIndex < Config.HiddenSize; previousIndex++)
                {
                    activation += recurrentWeights[recurrentOffset + previousIndex] * hiddenStates[previousHiddenOffset + previousIndex];
                }

                hiddenStates[currentHiddenOffset + hiddenIndex] = Math.Tanh(activation);
            }

            ProjectToLogits(hiddenStates.AsSpan(currentHiddenOffset, Config.HiddenSize), logits);
            SoftmaxInto(logits, probabilities.AsSpan(step * Config.VocabularySize, Config.VocabularySize));
            loss -= Math.Log(Math.Max(probabilities[step * Config.VocabularySize + targets[step]], 1e-12));
        }

        return loss;
    }

    private void BackwardTrainingPass(
        ReadOnlySpan<int> inputs,
        ReadOnlySpan<int> targets,
        double[] hiddenStates,
        double[] probabilities,
        GradientSet gradients)
    {
        var hiddenGradientFromFuture = new double[Config.HiddenSize];
        var currentHiddenGradient = new double[Config.HiddenSize];
        var activationGradient = new double[Config.HiddenSize];
        var nextHiddenGradient = new double[Config.HiddenSize];
        var logitsGradient = new double[Config.VocabularySize];

        for (var step = inputs.Length - 1; step >= 0; step--)
        {
            var previousHiddenOffset = step * Config.HiddenSize;
            var currentHiddenOffset = (step + 1) * Config.HiddenSize;
            var probabilityOffset = step * Config.VocabularySize;

            Array.Copy(probabilities, probabilityOffset, logitsGradient, 0, Config.VocabularySize);
            logitsGradient[targets[step]] -= 1.0;
            Array.Clear(currentHiddenGradient);

            for (var token = 0; token < Config.VocabularySize; token++)
            {
                var outputGradient = logitsGradient[token];
                gradients.OutputBias[token] += outputGradient;
                var outputOffset = token * Config.HiddenSize;

                for (var hiddenIndex = 0; hiddenIndex < Config.HiddenSize; hiddenIndex++)
                {
                    gradients.OutputWeights[outputOffset + hiddenIndex] +=
                        outputGradient * hiddenStates[currentHiddenOffset + hiddenIndex];
                    currentHiddenGradient[hiddenIndex] += outputWeights[outputOffset + hiddenIndex] * outputGradient;
                }
            }

            for (var hiddenIndex = 0; hiddenIndex < Config.HiddenSize; hiddenIndex++)
            {
                currentHiddenGradient[hiddenIndex] += hiddenGradientFromFuture[hiddenIndex];
                var hiddenValue = hiddenStates[currentHiddenOffset + hiddenIndex];
                activationGradient[hiddenIndex] = currentHiddenGradient[hiddenIndex] * (1.0 - hiddenValue * hiddenValue);
                gradients.HiddenBias[hiddenIndex] += activationGradient[hiddenIndex];
            }

            var tokenOffset = inputs[step] * Config.EmbeddingSize;
            for (var hiddenIndex = 0; hiddenIndex < Config.HiddenSize; hiddenIndex++)
            {
                var activationValue = activationGradient[hiddenIndex];
                var inputOffset = hiddenIndex * Config.EmbeddingSize;
                for (var embeddingIndex = 0; embeddingIndex < Config.EmbeddingSize; embeddingIndex++)
                {
                    gradients.InputWeights[inputOffset + embeddingIndex] +=
                        activationValue * embeddings[tokenOffset + embeddingIndex];
                    gradients.Embeddings[tokenOffset + embeddingIndex] +=
                        inputWeights[inputOffset + embeddingIndex] * activationValue;
                }

                var recurrentOffset = hiddenIndex * Config.HiddenSize;
                for (var previousIndex = 0; previousIndex < Config.HiddenSize; previousIndex++)
                {
                    gradients.RecurrentWeights[recurrentOffset + previousIndex] +=
                        activationValue * hiddenStates[previousHiddenOffset + previousIndex];
                }
            }

            Array.Clear(nextHiddenGradient);
            for (var hiddenIndex = 0; hiddenIndex < Config.HiddenSize; hiddenIndex++)
            {
                var recurrentOffset = hiddenIndex * Config.HiddenSize;
                for (var previousIndex = 0; previousIndex < Config.HiddenSize; previousIndex++)
                {
                    nextHiddenGradient[previousIndex] += recurrentWeights[recurrentOffset + previousIndex] * activationGradient[hiddenIndex];
                }
            }

            (hiddenGradientFromFuture, nextHiddenGradient) = (nextHiddenGradient, hiddenGradientFromFuture);
        }
    }

    private double[] ProjectToLogits(ReadOnlySpan<double> hiddenState)
    {
        var logits = new double[Config.VocabularySize];
        ProjectToLogits(hiddenState, logits);
        return logits;
    }

    private void ProjectToLogits(ReadOnlySpan<double> hiddenState, Span<double> logits)
    {
        for (var token = 0; token < Config.VocabularySize; token++)
        {
            var value = outputBias[token];
            var offset = token * Config.HiddenSize;
            for (var hiddenIndex = 0; hiddenIndex < Config.HiddenSize; hiddenIndex++)
            {
                value += outputWeights[offset + hiddenIndex] * hiddenState[hiddenIndex];
            }

            logits[token] = value;
        }
    }

    private void ApplyAdamW(
        double[] parameters,
        double[] gradients,
        double[] firstMoment,
        double[] secondMoment,
        double learningRate,
        double weightDecay)
    {
        var firstCorrection = 1.0 - Math.Pow(AdamBeta1, optimizerStep);
        var secondCorrection = 1.0 - Math.Pow(AdamBeta2, optimizerStep);

        for (var index = 0; index < parameters.Length; index++)
        {
            var gradient = gradients[index];
            firstMoment[index] = AdamBeta1 * firstMoment[index] + (1.0 - AdamBeta1) * gradient;
            secondMoment[index] = AdamBeta2 * secondMoment[index] + (1.0 - AdamBeta2) * gradient * gradient;
            var correctedFirst = firstMoment[index] / firstCorrection;
            var correctedSecond = secondMoment[index] / secondCorrection;
            parameters[index] -= learningRate *
                (correctedFirst / (Math.Sqrt(correctedSecond) + AdamEpsilon) + weightDecay * parameters[index]);
        }
    }

    private void InitializeWeights(Random random)
    {
        FillXavier(embeddings, Config.VocabularySize, Config.EmbeddingSize, random);
        FillXavier(inputWeights, Config.EmbeddingSize, Config.HiddenSize, random);
        FillXavier(recurrentWeights, Config.HiddenSize, Config.HiddenSize, random);
        FillXavier(outputWeights, Config.HiddenSize, Config.VocabularySize, random);
    }

    private static void FillXavier(double[] values, int fanIn, int fanOut, Random random)
    {
        var limit = Math.Sqrt(6.0 / (fanIn + fanOut));
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = (random.NextDouble() * 2.0 - 1.0) * limit;
        }
    }

    private static double[] Softmax(ReadOnlySpan<double> logits)
    {
        var output = new double[logits.Length];
        SoftmaxInto(logits, output);
        return output;
    }

    private static void SoftmaxInto(ReadOnlySpan<double> logits, Span<double> output)
    {
        var max = double.NegativeInfinity;
        for (var index = 0; index < logits.Length; index++)
        {
            max = Math.Max(max, logits[index]);
        }

        var sum = 0.0;
        for (var index = 0; index < logits.Length; index++)
        {
            var value = Math.Exp(Math.Clamp(logits[index] - max, -60, 60));
            output[index] = value;
            sum += value;
        }

        var denominator = Math.Max(sum, 1e-12);
        for (var index = 0; index < output.Length; index++)
        {
            output[index] /= denominator;
        }
    }

    private void ValidateSequence(ReadOnlySpan<int> inputs, ReadOnlySpan<int> targets)
    {
        if (inputs.Length == 0 || inputs.Length != targets.Length)
        {
            throw new ArgumentException("Input and target sequences must have the same non-zero length.");
        }

        if (inputs.Length > Config.SequenceLength)
        {
            throw new ArgumentOutOfRangeException(nameof(inputs), "Sequence exceeds the configured maximum length.");
        }

        foreach (var token in inputs)
        {
            ValidateToken(token);
        }

        foreach (var token in targets)
        {
            ValidateToken(token);
        }
    }

    private void ValidateToken(int tokenId)
    {
        if (tokenId < 0 || tokenId >= Config.VocabularySize)
        {
            throw new ArgumentOutOfRangeException(nameof(tokenId), tokenId, "Token is outside the vocabulary.");
        }
    }

    private static void CopyExact(double[] source, double[] destination, string name)
    {
        if (source.Length != destination.Length)
        {
            throw new InvalidDataException($"Checkpoint array {name} has an invalid length.");
        }

        Array.Copy(source, destination, source.Length);
    }

    private sealed class GradientSet
    {
        public GradientSet(RecurrentLanguageModel model)
        {
            Embeddings = new double[model.embeddings.Length];
            InputWeights = new double[model.inputWeights.Length];
            RecurrentWeights = new double[model.recurrentWeights.Length];
            HiddenBias = new double[model.hiddenBias.Length];
            OutputWeights = new double[model.outputWeights.Length];
            OutputBias = new double[model.outputBias.Length];
        }

        public double[] Embeddings { get; }
        public double[] InputWeights { get; }
        public double[] RecurrentWeights { get; }
        public double[] HiddenBias { get; }
        public double[] OutputWeights { get; }
        public double[] OutputBias { get; }

        public void Scale(double factor)
        {
            ScaleArray(Embeddings, factor);
            ScaleArray(InputWeights, factor);
            ScaleArray(RecurrentWeights, factor);
            ScaleArray(HiddenBias, factor);
            ScaleArray(OutputWeights, factor);
            ScaleArray(OutputBias, factor);
        }

        public void ClipByGlobalNorm(double maximumNorm)
        {
            var squaredNorm = SumSquares(Embeddings) + SumSquares(InputWeights) +
                              SumSquares(RecurrentWeights) + SumSquares(HiddenBias) +
                              SumSquares(OutputWeights) + SumSquares(OutputBias);
            var norm = Math.Sqrt(squaredNorm);
            if (norm <= maximumNorm || norm <= 0)
            {
                return;
            }

            Scale(maximumNorm / norm);
        }

        private static void ScaleArray(double[] values, double factor)
        {
            for (var index = 0; index < values.Length; index++)
            {
                values[index] *= factor;
            }
        }

        private static double SumSquares(double[] values)
        {
            var result = 0.0;
            foreach (var value in values)
            {
                result += value * value;
            }

            return result;
        }
    }
}
