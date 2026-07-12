namespace Thoth.Model;

/// <summary>
/// A decoder-only Transformer foundation implemented with deterministic CPU arrays.
/// All weights are initialized from a seed. The current trainer updates the language
/// modeling head and bias with AdamW; the full attention stack participates in the
/// forward pass and checkpoint round-trip.
/// </summary>
public sealed class TransformerLanguageModel : ITrainableLanguageModel
{
    private const double AdamBeta1 = 0.9;
    private const double AdamBeta2 = 0.999;
    private const double AdamEpsilon = 1e-8;
    private const double RmsNormEpsilon = 1e-6;

    private readonly double[] tokenEmbeddings;
    private readonly double[] finalNormWeight;
    private readonly double[] lmHead;
    private readonly double[] outputBias;
    private readonly double[] lmHeadFirstMoment;
    private readonly double[] lmHeadSecondMoment;
    private readonly double[] outputBiasFirstMoment;
    private readonly double[] outputBiasSecondMoment;
    private readonly TransformerLayer[] layers;

    private long optimizerStep;

    public TransformerLanguageModel(TransformerConfig config)
    {
        config.Validate();
        Config = config;
        tokenEmbeddings = new double[config.VocabularySize * config.Width];
        finalNormWeight = CreateOnes(config.Width);
        lmHead = new double[config.VocabularySize * config.Width];
        outputBias = new double[config.VocabularySize];
        lmHeadFirstMoment = new double[lmHead.Length];
        lmHeadSecondMoment = new double[lmHead.Length];
        outputBiasFirstMoment = new double[outputBias.Length];
        outputBiasSecondMoment = new double[outputBias.Length];
        layers = Enumerable.Range(0, config.LayerCount)
            .Select(_ => new TransformerLayer(config))
            .ToArray();

        InitializeWeights(new Random(config.Seed));
    }

    public TransformerConfig Config { get; }

    public ModelArchitecture Architecture => ModelArchitecture.DecoderOnlyTransformer;

    public int VocabularySize => Config.VocabularySize;

    public int ContextLength => Config.ContextLength;

    public long OptimizerStep => optimizerStep;

    public int ParameterCount =>
        tokenEmbeddings.Length + finalNormWeight.Length + lmHead.Length + outputBias.Length +
        layers.Sum(layer => layer.ParameterCount);

    public ModelForwardResult Forward(int[,] inputTokenIds, int[,]? targetTokenIds = null)
    {
        var forward = ForwardInternal(inputTokenIds, captureFinalHidden: false);
        var loss = targetTokenIds is null ? (double?)null : CrossEntropyLoss(forward.Logits, targetTokenIds);
        return new ModelForwardResult(forward.Logits, loss);
    }

    public double EvaluateBatch(int[,] inputTokenIds, int[,] targetTokenIds) =>
        Forward(inputTokenIds, targetTokenIds).Loss ?? double.NaN;

    public double TrainBatch(
        int[,] inputTokenIds,
        int[,] targetTokenIds,
        double learningRate,
        double weightDecay = 0.01,
        double gradientClip = 1.0)
    {
        if (!double.IsFinite(learningRate) || learningRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(learningRate), "Learning rate must be finite and positive.");
        }

        if (!double.IsFinite(weightDecay) || weightDecay < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(weightDecay), "Weight decay must be finite and non-negative.");
        }

        if (!double.IsFinite(gradientClip) || gradientClip <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gradientClip), "Gradient clip must be finite and positive.");
        }

        var forward = ForwardInternal(inputTokenIds, captureFinalHidden: true);
        var logits = forward.Logits;
        var finalHidden = forward.FinalHidden
            ?? throw new InvalidOperationException("Internal error: final hidden activations were not captured.");
        var batch = logits.GetLength(0);
        var sequence = logits.GetLength(1);
        var count = checked(batch * sequence);
        ValidateTargetShape(targetTokenIds, batch, sequence);

        var headGradient = new double[lmHead.Length];
        var biasGradient = new double[outputBias.Length];
        var loss = 0.0;
        var probabilities = new double[VocabularySize];

        for (var row = 0; row < batch; row++)
        {
            for (var position = 0; position < sequence; position++)
            {
                var target = targetTokenIds[row, position];
                ValidateToken(target);
                SoftmaxInto(logits, row, position, probabilities);
                loss -= Math.Log(Math.Max(probabilities[target], 1e-12));

                for (var token = 0; token < VocabularySize; token++)
                {
                    var error = probabilities[token] - (token == target ? 1.0 : 0.0);
                    biasGradient[token] += error / count;
                    var headOffset = token * Config.Width;
                    for (var width = 0; width < Config.Width; width++)
                    {
                        headGradient[headOffset + width] += error * finalHidden[row, position, width] / count;
                    }
                }
            }
        }

        loss /= count;
        if (!double.IsFinite(loss))
        {
            throw new InvalidOperationException("Training diverged: loss became non-finite.");
        }

        ClipByGlobalNorm(headGradient, biasGradient, gradientClip);
        EnsureFiniteGradients(headGradient, biasGradient);
        optimizerStep++;
        ApplyAdamW(lmHead, headGradient, lmHeadFirstMoment, lmHeadSecondMoment, learningRate, weightDecay);
        ApplyAdamW(outputBias, biasGradient, outputBiasFirstMoment, outputBiasSecondMoment, learningRate, 0);
        return loss;
    }

    public ModelGenerationState CreateGenerationState(IEnumerable<int> promptTokens) =>
        new(promptTokens.TakeLast(ContextLength).ToArray(), ContextLength);

    public double[] NextTokenLogits(IReadOnlyList<int> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var context = tokens.Count == 0
            ? [0]
            : tokens.TakeLast(ContextLength).ToArray();
        var input = new int[1, context.Length];
        for (var index = 0; index < context.Length; index++)
        {
            input[0, index] = context[index];
        }

        var logits = Forward(input).Logits;
        var last = context.Length - 1;
        var result = new double[VocabularySize];
        for (var token = 0; token < VocabularySize; token++)
        {
            result[token] = logits[0, last, token];
        }

        return result;
    }

    public TransformerModelState ExportState(bool includeOptimizer = true) =>
        new(
            Config,
            optimizerStep,
            (double[])tokenEmbeddings.Clone(),
            (double[])finalNormWeight.Clone(),
            (double[])lmHead.Clone(),
            (double[])outputBias.Clone(),
            includeOptimizer ? (double[])lmHeadFirstMoment.Clone() : new double[lmHead.Length],
            includeOptimizer ? (double[])lmHeadSecondMoment.Clone() : new double[lmHead.Length],
            includeOptimizer ? (double[])outputBiasFirstMoment.Clone() : new double[outputBias.Length],
            includeOptimizer ? (double[])outputBiasSecondMoment.Clone() : new double[outputBias.Length],
            layers.Select(layer => layer.ExportState()).ToArray());

    public static TransformerLanguageModel FromState(TransformerModelState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var model = new TransformerLanguageModel(state.Config);
        model.ImportState(state);
        return model;
    }

    public void ImportState(TransformerModelState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Config != Config)
        {
            throw new InvalidDataException("Checkpoint transformer configuration does not match the current model.");
        }

        CopyExact(state.TokenEmbeddings, tokenEmbeddings, nameof(state.TokenEmbeddings));
        CopyExact(state.FinalNormWeight, finalNormWeight, nameof(state.FinalNormWeight));
        CopyExact(state.LmHead, lmHead, nameof(state.LmHead));
        CopyExact(state.OutputBias, outputBias, nameof(state.OutputBias));
        CopyExact(state.LmHeadFirstMoment, lmHeadFirstMoment, nameof(state.LmHeadFirstMoment));
        CopyExact(state.LmHeadSecondMoment, lmHeadSecondMoment, nameof(state.LmHeadSecondMoment));
        CopyExact(state.OutputBiasFirstMoment, outputBiasFirstMoment, nameof(state.OutputBiasFirstMoment));
        CopyExact(state.OutputBiasSecondMoment, outputBiasSecondMoment, nameof(state.OutputBiasSecondMoment));

        if (state.Layers.Count != layers.Length)
        {
            throw new InvalidDataException("Checkpoint transformer layer count does not match the model.");
        }

        for (var index = 0; index < layers.Length; index++)
        {
            layers[index].ImportState(state.Layers[index]);
        }

        optimizerStep = Math.Max(0, state.OptimizerStep);
    }

    private ForwardComputation ForwardInternal(int[,] inputTokenIds, bool captureFinalHidden)
    {
        ValidateInputShape(inputTokenIds);
        var batch = inputTokenIds.GetLength(0);
        var sequence = inputTokenIds.GetLength(1);
        var logits = new double[batch, sequence, VocabularySize];
        var captured = captureFinalHidden ? new double[batch, sequence, Config.Width] : null;

        for (var row = 0; row < batch; row++)
        {
            var hidden = new double[sequence, Config.Width];
            for (var position = 0; position < sequence; position++)
            {
                var token = inputTokenIds[row, position];
                ValidateToken(token);
                var embeddingOffset = token * Config.Width;
                for (var width = 0; width < Config.Width; width++)
                {
                    hidden[position, width] = tokenEmbeddings[embeddingOffset + width];
                }
            }

            foreach (var layer in layers)
            {
                hidden = layer.Forward(hidden, Config);
            }

            for (var position = 0; position < sequence; position++)
            {
                var normalized = RmsNormalize(hidden, position, finalNormWeight, Config.Width);
                if (captured is not null)
                {
                    for (var width = 0; width < Config.Width; width++)
                    {
                        captured[row, position, width] = normalized[width];
                    }
                }

                ProjectLogits(normalized, logits, row, position);
            }
        }

        return new ForwardComputation(logits, captured);
    }

    private void ProjectLogits(double[] hidden, double[,,] logits, int row, int position)
    {
        for (var token = 0; token < VocabularySize; token++)
        {
            var value = outputBias[token];
            var headOffset = token * Config.Width;
            for (var width = 0; width < Config.Width; width++)
            {
                value += lmHead[headOffset + width] * hidden[width];
            }

            if (!double.IsFinite(value))
            {
                throw new InvalidOperationException("Transformer forward pass produced a non-finite logit.");
            }

            logits[row, position, token] = value;
        }
    }

    private double CrossEntropyLoss(double[,,] logits, int[,] targetTokenIds)
    {
        var batch = logits.GetLength(0);
        var sequence = logits.GetLength(1);
        ValidateTargetShape(targetTokenIds, batch, sequence);
        var probabilities = new double[VocabularySize];
        var loss = 0.0;

        for (var row = 0; row < batch; row++)
        {
            for (var position = 0; position < sequence; position++)
            {
                var target = targetTokenIds[row, position];
                ValidateToken(target);
                SoftmaxInto(logits, row, position, probabilities);
                loss -= Math.Log(Math.Max(probabilities[target], 1e-12));
            }
        }

        loss /= batch * sequence;
        if (!double.IsFinite(loss))
        {
            throw new InvalidOperationException("Transformer forward pass produced a non-finite loss.");
        }

        return loss;
    }

    private void InitializeWeights(Random random)
    {
        FillNormal(tokenEmbeddings, random, 0.02);
        FillNormal(lmHead, random, 0.02);
        foreach (var layer in layers)
        {
            layer.Initialize(random, Config);
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

    private static double[] RmsNormalize(double[,] values, int row, double[] weight, int width)
    {
        var meanSquare = 0.0;
        for (var index = 0; index < width; index++)
        {
            meanSquare += values[row, index] * values[row, index];
        }

        var scale = 1.0 / Math.Sqrt(meanSquare / width + RmsNormEpsilon);
        var output = new double[width];
        for (var index = 0; index < width; index++)
        {
            output[index] = values[row, index] * scale * weight[index];
        }

        return output;
    }

    private static void Project(double[] input, double[] weight, Span<double> output, int inputWidth, int outputWidth)
    {
        output.Clear();
        for (var inputIndex = 0; inputIndex < inputWidth; inputIndex++)
        {
            var value = input[inputIndex];
            var offset = inputIndex * outputWidth;
            for (var outputIndex = 0; outputIndex < outputWidth; outputIndex++)
            {
                output[outputIndex] += value * weight[offset + outputIndex];
            }
        }
    }

    private static void ProjectToMatrix(
        double[] input,
        double[] weight,
        double[,] output,
        int outputRow,
        int inputWidth,
        int outputWidth)
    {
        for (var outputIndex = 0; outputIndex < outputWidth; outputIndex++)
        {
            output[outputRow, outputIndex] = 0;
        }

        for (var inputIndex = 0; inputIndex < inputWidth; inputIndex++)
        {
            var value = input[inputIndex];
            var offset = inputIndex * outputWidth;
            for (var outputIndex = 0; outputIndex < outputWidth; outputIndex++)
            {
                output[outputRow, outputIndex] += value * weight[offset + outputIndex];
            }
        }
    }

    private static void SoftmaxInto(double[,,] logits, int row, int position, Span<double> output)
    {
        var max = double.NegativeInfinity;
        for (var token = 0; token < output.Length; token++)
        {
            max = Math.Max(max, logits[row, position, token]);
        }

        var sum = 0.0;
        for (var token = 0; token < output.Length; token++)
        {
            var value = Math.Exp(Math.Clamp(logits[row, position, token] - max, -60, 60));
            output[token] = value;
            sum += value;
        }

        if (!double.IsFinite(sum) || sum <= 0)
        {
            throw new InvalidOperationException("Transformer softmax produced a non-finite denominator.");
        }

        for (var token = 0; token < output.Length; token++)
        {
            output[token] /= sum;
        }
    }

    private static void SoftmaxInto(ReadOnlySpan<double> scores, Span<double> output, int count)
    {
        var max = double.NegativeInfinity;
        for (var index = 0; index < count; index++)
        {
            max = Math.Max(max, scores[index]);
        }

        var sum = 0.0;
        for (var index = 0; index < count; index++)
        {
            var value = Math.Exp(Math.Clamp(scores[index] - max, -60, 60));
            output[index] = value;
            sum += value;
        }

        if (!double.IsFinite(sum) || sum <= 0)
        {
            throw new InvalidOperationException("Attention softmax produced a non-finite denominator.");
        }

        for (var index = 0; index < count; index++)
        {
            output[index] /= sum;
        }
    }

    private static double Silu(double value) => value / (1.0 + Math.Exp(-Math.Clamp(value, -60, 60)));

    private static void ApplyRope(Span<double> values, int position, int headDimension)
    {
        for (var index = 0; index < headDimension; index += 2)
        {
            var theta = Math.Pow(10000.0, -(double)index / headDimension);
            var angle = position * theta;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            var first = values[index];
            var second = values[index + 1];
            values[index] = first * cos - second * sin;
            values[index + 1] = first * sin + second * cos;
        }
    }

    private static void ApplyRope(double[,] values, int row, int offset, int headDimension)
    {
        for (var index = 0; index < headDimension; index += 2)
        {
            var theta = Math.Pow(10000.0, -(double)index / headDimension);
            var angle = row * theta;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            var first = values[row, offset + index];
            var second = values[row, offset + index + 1];
            values[row, offset + index] = first * cos - second * sin;
            values[row, offset + index + 1] = first * sin + second * cos;
        }
    }

    private static double[] ReadRow(double[,] values, int row, int width)
    {
        var output = new double[width];
        for (var index = 0; index < width; index++)
        {
            output[index] = values[row, index];
        }

        return output;
    }

    private static double[] CreateOnes(int length)
    {
        var values = new double[length];
        Array.Fill(values, 1.0);
        return values;
    }

    private static void FillNormal(double[] values, Random random, double standardDeviation)
    {
        for (var index = 0; index < values.Length; index += 2)
        {
            var u1 = Math.Max(random.NextDouble(), 1e-12);
            var u2 = random.NextDouble();
            var radius = Math.Sqrt(-2.0 * Math.Log(u1)) * standardDeviation;
            var angle = 2.0 * Math.PI * u2;
            values[index] = radius * Math.Cos(angle);
            if (index + 1 < values.Length)
            {
                values[index + 1] = radius * Math.Sin(angle);
            }
        }
    }

    private static void ClipByGlobalNorm(double[] first, double[] second, double maximumNorm)
    {
        var squared = SumSquares(first) + SumSquares(second);
        var norm = Math.Sqrt(squared);
        if (norm <= maximumNorm || norm <= 0)
        {
            return;
        }

        var scale = maximumNorm / norm;
        Scale(first, scale);
        Scale(second, scale);
    }

    private static void EnsureFiniteGradients(params double[][] gradients)
    {
        foreach (var gradient in gradients)
        {
            foreach (var value in gradient)
            {
                if (!double.IsFinite(value))
                {
                    throw new InvalidOperationException("Training diverged: gradients became non-finite.");
                }
            }
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

    private static void Scale(double[] values, double factor)
    {
        for (var index = 0; index < values.Length; index++)
        {
            values[index] *= factor;
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

    private void ValidateInputShape(int[,] inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.GetLength(0) < 1 || inputs.GetLength(1) < 1)
        {
            throw new ArgumentException("Input batch must be non-empty.", nameof(inputs));
        }

        if (inputs.GetLength(1) > ContextLength)
        {
            throw new ArgumentOutOfRangeException(nameof(inputs), "Input sequence exceeds model context length.");
        }
    }

    private static void ValidateTargetShape(int[,] targets, int batch, int sequence)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.GetLength(0) != batch || targets.GetLength(1) != sequence)
        {
            throw new ArgumentException("Target batch shape must match logits shape.", nameof(targets));
        }
    }

    private void ValidateToken(int tokenId)
    {
        if (tokenId < 0 || tokenId >= VocabularySize)
        {
            throw new ArgumentOutOfRangeException(nameof(tokenId), tokenId, "Token is outside the vocabulary.");
        }
    }

    private sealed record ForwardComputation(double[,,] Logits, double[,,]? FinalHidden);

    private sealed class TransformerLayer
    {
        public TransformerLayer(TransformerConfig config)
        {
            AttentionNormWeight = CreateOnes(config.Width);
            FeedForwardNormWeight = CreateOnes(config.Width);
            QueryWeight = new double[config.Width * config.Width];
            KeyWeight = new double[config.Width * config.Width];
            ValueWeight = new double[config.Width * config.Width];
            OutputWeight = new double[config.Width * config.Width];
            GateWeight = new double[config.Width * config.FeedForwardSize];
            UpWeight = new double[config.Width * config.FeedForwardSize];
            DownWeight = new double[config.FeedForwardSize * config.Width];
        }

        public double[] AttentionNormWeight { get; }

        public double[] FeedForwardNormWeight { get; }

        public double[] QueryWeight { get; }

        public double[] KeyWeight { get; }

        public double[] ValueWeight { get; }

        public double[] OutputWeight { get; }

        public double[] GateWeight { get; }

        public double[] UpWeight { get; }

        public double[] DownWeight { get; }

        public int ParameterCount =>
            AttentionNormWeight.Length + FeedForwardNormWeight.Length +
            QueryWeight.Length + KeyWeight.Length + ValueWeight.Length + OutputWeight.Length +
            GateWeight.Length + UpWeight.Length + DownWeight.Length;

        public void Initialize(Random random, TransformerConfig config)
        {
            var projectionScale = 1.0 / Math.Sqrt(config.Width);
            FillNormal(QueryWeight, random, projectionScale);
            FillNormal(KeyWeight, random, projectionScale);
            FillNormal(ValueWeight, random, projectionScale);
            FillNormal(OutputWeight, random, projectionScale);
            FillNormal(GateWeight, random, projectionScale);
            FillNormal(UpWeight, random, projectionScale);
            FillNormal(DownWeight, random, 1.0 / Math.Sqrt(config.FeedForwardSize));
        }

        public double[,] Forward(double[,] input, TransformerConfig config)
        {
            var sequence = input.GetLength(0);
            var attention = Attention(input, config);
            var afterAttention = new double[sequence, config.Width];
            for (var position = 0; position < sequence; position++)
            {
                for (var width = 0; width < config.Width; width++)
                {
                    afterAttention[position, width] = input[position, width] + attention[position, width];
                }
            }

            var feedForward = FeedForward(afterAttention, config);
            var output = new double[sequence, config.Width];
            for (var position = 0; position < sequence; position++)
            {
                for (var width = 0; width < config.Width; width++)
                {
                    output[position, width] = afterAttention[position, width] + feedForward[position, width];
                }
            }

            return output;
        }

        public TransformerLayerState ExportState() =>
            new(
                (double[])AttentionNormWeight.Clone(),
                (double[])FeedForwardNormWeight.Clone(),
                (double[])QueryWeight.Clone(),
                (double[])KeyWeight.Clone(),
                (double[])ValueWeight.Clone(),
                (double[])OutputWeight.Clone(),
                (double[])GateWeight.Clone(),
                (double[])UpWeight.Clone(),
                (double[])DownWeight.Clone());

        public void ImportState(TransformerLayerState state)
        {
            CopyExact(state.AttentionNormWeight, AttentionNormWeight, nameof(state.AttentionNormWeight));
            CopyExact(state.FeedForwardNormWeight, FeedForwardNormWeight, nameof(state.FeedForwardNormWeight));
            CopyExact(state.QueryWeight, QueryWeight, nameof(state.QueryWeight));
            CopyExact(state.KeyWeight, KeyWeight, nameof(state.KeyWeight));
            CopyExact(state.ValueWeight, ValueWeight, nameof(state.ValueWeight));
            CopyExact(state.OutputWeight, OutputWeight, nameof(state.OutputWeight));
            CopyExact(state.GateWeight, GateWeight, nameof(state.GateWeight));
            CopyExact(state.UpWeight, UpWeight, nameof(state.UpWeight));
            CopyExact(state.DownWeight, DownWeight, nameof(state.DownWeight));
        }

        private double[,] Attention(double[,] input, TransformerConfig config)
        {
            var sequence = input.GetLength(0);
            var width = config.Width;
            var headDimension = width / config.HeadCount;
            var q = new double[sequence, width];
            var k = new double[sequence, width];
            var v = new double[sequence, width];

            for (var position = 0; position < sequence; position++)
            {
                var norm = RmsNormalize(input, position, AttentionNormWeight, width);
                ProjectToMatrix(norm, QueryWeight, q, position, width, width);
                ProjectToMatrix(norm, KeyWeight, k, position, width, width);
                ProjectToMatrix(norm, ValueWeight, v, position, width, width);

                for (var head = 0; head < config.HeadCount; head++)
                {
                    ApplyRope(q, position, head * headDimension, headDimension);
                    ApplyRope(k, position, head * headDimension, headDimension);
                }
            }

            var context = new double[sequence, width];
            var scores = new double[sequence];
            var probabilities = new double[sequence];
            var scale = 1.0 / Math.Sqrt(headDimension);

            for (var head = 0; head < config.HeadCount; head++)
            {
                var offset = head * headDimension;
                for (var position = 0; position < sequence; position++)
                {
                    for (var source = 0; source <= position; source++)
                    {
                        var dot = 0.0;
                        for (var index = 0; index < headDimension; index++)
                        {
                            dot += q[position, offset + index] * k[source, offset + index];
                        }

                        scores[source] = dot * scale;
                    }

                    SoftmaxInto(scores, probabilities, position + 1);
                    for (var source = 0; source <= position; source++)
                    {
                        var probability = probabilities[source];
                        for (var index = 0; index < headDimension; index++)
                        {
                            context[position, offset + index] += probability * v[source, offset + index];
                        }
                    }
                }
            }

            var projected = new double[sequence, width];
            for (var position = 0; position < sequence; position++)
            {
                ProjectToMatrix(ReadRow(context, position, width), OutputWeight, projected, position, width, width);
            }

            return projected;
        }

        private double[,] FeedForward(double[,] input, TransformerConfig config)
        {
            var sequence = input.GetLength(0);
            var output = new double[sequence, config.Width];
            var gate = new double[config.FeedForwardSize];
            var up = new double[config.FeedForwardSize];
            var product = new double[config.FeedForwardSize];

            for (var position = 0; position < sequence; position++)
            {
                var norm = RmsNormalize(input, position, FeedForwardNormWeight, config.Width);
                Project(norm, GateWeight, gate, config.Width, config.FeedForwardSize);
                Project(norm, UpWeight, up, config.Width, config.FeedForwardSize);
                for (var index = 0; index < config.FeedForwardSize; index++)
                {
                    product[index] = Silu(gate[index]) * up[index];
                }

                ProjectToMatrix(product, DownWeight, output, position, config.FeedForwardSize, config.Width);
            }

            return output;
        }
    }
}
