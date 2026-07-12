using TorchSharp;
using static TorchSharp.torch;

namespace Thoth.Model;

public sealed class TorchTransformerLanguageModel : IDisposable
{
    private const double RmsEpsilon = 1e-5;
    private const double AdamBeta1 = 0.9;
    private const double AdamBeta2 = 0.999;
    private const double AdamEpsilon = 1e-8;

    private readonly List<ParameterState> parameters = [];
    private readonly TorchTransformerLayer[] layers;
    private readonly Device device;
    private bool disposed;

    public TorchTransformerLanguageModel(TorchTransformerConfig config)
    {
        config.Validate();
        Config = config;
        device = ResolveDevice(config.Device);
        random.manual_seed(config.Seed);

        TokenEmbedding = CreateParameter("token_embedding", [config.VocabularySize, config.Width], 0.02);
        FinalNormWeight = CreateParameter("final_norm", [config.Width], 1.0, randomNormal: false);
        LmHead = config.TieOutputEmbeddings
            ? TokenEmbedding
            : CreateParameter("lm_head", [config.VocabularySize, config.Width], 0.02);
        layers = Enumerable.Range(0, config.LayerCount)
            .Select(index => new TorchTransformerLayer(index, config, CreateParameter))
            .ToArray();
    }

    public TorchTransformerConfig Config { get; }

    public long OptimizerStep { get; private set; }

    public Tensor TokenEmbedding { get; private set; }

    public Tensor FinalNormWeight { get; private set; }

    public Tensor LmHead { get; private set; }

    public long ParameterCount => parameters.Sum(parameter => parameter.Value.numel());

    public IReadOnlyDictionary<string, Tensor> NamedParameters() =>
        parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.Value, StringComparer.Ordinal);

    public IReadOnlyDictionary<string, bool> ParameterGradientFiniteMap()
    {
        return parameters.ToDictionary(
            parameter => parameter.Name,
            parameter =>
            {
                var grad = parameter.Value.grad;
                return grad is not null &&
                       grad.numel() > 0 &&
                       torch.isfinite(grad).all().ToBoolean();
            },
            StringComparer.Ordinal);
    }

    public Tensor Forward(Tensor tokenIds, bool training = false)
    {
        ThrowIfDisposed();
        if (tokenIds.shape.Length != 2)
        {
            throw new ArgumentException("Token tensor must have shape [batch, sequence].", nameof(tokenIds));
        }

        var sequenceLength = (int)tokenIds.shape[1];
        if (sequenceLength > Config.ContextLength)
        {
            throw new ArgumentOutOfRangeException(nameof(tokenIds), "Input sequence exceeds configured context length.");
        }

        var flatIds = tokenIds.reshape([-1]);
        var x = TokenEmbedding.index_select(0, flatIds).reshape([tokenIds.shape[0], tokenIds.shape[1], Config.Width]);
        foreach (var layer in layers)
        {
            x = layer.Forward(x, Config, training, device);
        }

        x = RmsNorm(x, FinalNormWeight);
        return torch.matmul(x, LmHead.transpose(0, 1));
    }

    public Tensor Loss(Tensor inputTokenIds, Tensor targetTokenIds)
    {
        var logits = Forward(inputTokenIds, training: true);
        var flatLogits = logits.reshape([-1, Config.VocabularySize]);
        var flatTargets = targetTokenIds.reshape([-1]);
        return torch.nn.functional.cross_entropy(flatLogits, flatTargets, ignore_index: Config.PaddingToken);
    }

    public double TrainBatch(
        long[,] inputTokenIds,
        long[,] targetTokenIds,
        double learningRate,
        double weightDecay = 0,
        double gradientClip = 1.0)
    {
        var inputs = TensorFrom(inputTokenIds);
        var targets = TensorFrom(targetTokenIds);
        return TrainBatch(inputs, targets, learningRate, weightDecay, gradientClip);
    }

    public double TrainBatch(
        Tensor inputTokenIds,
        Tensor targetTokenIds,
        double learningRate,
        double weightDecay = 0,
        double gradientClip = 1.0)
    {
        ZeroGrad();
        var value = AccumulateGradients(inputTokenIds, targetTokenIds);
        ApplyGradients(learningRate, weightDecay, gradientClip);
        return value;
    }

    public void BeginGradientAccumulation() => ZeroGrad();

    public double AccumulateGradients(Tensor inputTokenIds, Tensor targetTokenIds)
    {
        using var loss = Loss(inputTokenIds, targetTokenIds);
        var value = loss.ToDouble();
        if (!double.IsFinite(value))
        {
            throw new InvalidOperationException("Torch Transformer training diverged: loss is non-finite.");
        }

        loss.backward();
        return value;
    }

    public void ApplyGradients(
        double learningRate,
        double weightDecay = 0,
        double gradientClip = 1.0) =>
        StepAdamW(learningRate, weightDecay, gradientClip);

    public Tensor TensorFrom(long[,] values) =>
        torch.tensor(values, dtype: ScalarType.Int64, device: device);

    public float[] NextTokenLogits(IReadOnlyList<int> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Count == 0)
        {
            throw new ArgumentException("At least one token is required.", nameof(tokens));
        }

        var context = tokens.TakeLast(Config.ContextLength).ToArray();
        var values = new long[1, context.Length];
        for (var index = 0; index < context.Length; index++)
        {
            values[0, index] = context[index];
        }

        using var noGrad = torch.no_grad();
        using var input = TensorFrom(values);
        using var logits = Forward(input, training: false);
        using var last = logits[0, context.Length - 1].detach().cpu().reshape([-1]);
        return last.data<float>().ToArray();
    }

    public Tensor CloneParameter(string name)
    {
        var parameter = parameters.First(parameter => parameter.Name == name);
        return parameter.Value.detach().clone();
    }

    public TorchTransformerState ExportState() =>
        new(
            Config,
            OptimizerStep,
            parameters.Select(parameter => new TorchParameterSnapshot(
                parameter.Name,
                parameter.Value.shape.ToArray(),
                ToArray(parameter.Value),
                ToArray(parameter.FirstMoment),
                ToArray(parameter.SecondMoment))).ToArray());

    public static TorchTransformerLanguageModel FromState(TorchTransformerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var model = new TorchTransformerLanguageModel(state.Config);
        model.LoadState(state);
        return model;
    }

    public static double MaxAbsDifference(Tensor left, Tensor right) =>
        (left - right).abs().max().ToDouble();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        foreach (var parameter in parameters)
        {
            parameter.Dispose();
        }

        disposed = true;
    }

    private Tensor CreateParameter(string name, long[] shape, double scale, bool randomNormal = true)
    {
        var value = randomNormal
            ? torch.randn(shape, dtype: ScalarType.Float32, device: device) * scale
            : torch.full(shape, scale, dtype: ScalarType.Float32, device: device);
        value = value.detach().requires_grad_(true);
        var state = new ParameterState(name, value);
        parameters.Add(state);
        return state.Value;
    }

    private void ZeroGrad()
    {
        foreach (var parameter in parameters)
        {
            var grad = parameter.Value.grad;
            if (grad is not null)
            {
                grad.zero_();
            }
        }
    }

    private void StepAdamW(double learningRate, double weightDecay, double gradientClip)
    {
        var gradNorm = GradientNorm();
        var clipScale = gradientClip > 0 && gradNorm > gradientClip
            ? gradientClip / (gradNorm + 1e-12)
            : 1.0;
        var step = OptimizerStep + 1;

        using var _ = torch.no_grad();
        foreach (var parameter in parameters)
        {
            var grad = parameter.Value.grad;
            if (grad is null)
            {
                throw new InvalidOperationException($"Parameter {parameter.Name} did not receive a gradient.");
            }

            if (!torch.isfinite(grad).all().ToBoolean())
            {
                throw new InvalidOperationException($"Parameter {parameter.Name} received a non-finite gradient.");
            }

            var clipped = grad * clipScale;
            parameter.FirstMoment = parameter.FirstMoment * AdamBeta1 + clipped * (1 - AdamBeta1);
            parameter.SecondMoment = parameter.SecondMoment * AdamBeta2 + clipped.pow(2) * (1 - AdamBeta2);
            var firstHat = parameter.FirstMoment / (1 - Math.Pow(AdamBeta1, step));
            var secondHat = parameter.SecondMoment / (1 - Math.Pow(AdamBeta2, step));
            var update = firstHat / (secondHat.sqrt() + AdamEpsilon);
            if (weightDecay > 0)
            {
                update += parameter.Value * weightDecay;
            }

            parameter.Value = (parameter.Value - update * learningRate).detach().requires_grad_(true);
        }

        RebindParameterFields();
        OptimizerStep = step;
    }

    private double GradientNorm()
    {
        double sum = 0;
        foreach (var parameter in parameters)
        {
            var grad = parameter.Value.grad;
            if (grad is null)
            {
                continue;
            }

            sum += grad.pow(2).sum().ToDouble();
        }

        return Math.Sqrt(sum);
    }

    private void RebindParameterFields()
    {
        TokenEmbedding = FindParameter("token_embedding");
        FinalNormWeight = FindParameter("final_norm");
        LmHead = Config.TieOutputEmbeddings ? TokenEmbedding : FindParameter("lm_head");
        foreach (var layer in layers)
        {
            layer.Rebind(parameters);
        }
    }

    private void LoadState(TorchTransformerState state)
    {
        if (state.Parameters.Count != parameters.Count)
        {
            throw new InvalidDataException("Torch Transformer checkpoint parameter count does not match the model.");
        }

        foreach (var snapshot in state.Parameters)
        {
            var parameter = parameters.FirstOrDefault(parameter => parameter.Name == snapshot.Name)
                            ?? throw new InvalidDataException($"Torch Transformer checkpoint parameter is missing: {snapshot.Name}");
            if (!parameter.Value.shape.SequenceEqual(snapshot.Shape))
            {
                throw new InvalidDataException($"Torch Transformer checkpoint shape mismatch for {snapshot.Name}.");
            }

            parameter.Value.Dispose();
            parameter.FirstMoment.Dispose();
            parameter.SecondMoment.Dispose();
            parameter.Value = TensorFrom(snapshot.Value, snapshot.Shape, requiresGrad: true);
            parameter.FirstMoment = TensorFrom(snapshot.FirstMoment, snapshot.Shape, requiresGrad: false);
            parameter.SecondMoment = TensorFrom(snapshot.SecondMoment, snapshot.Shape, requiresGrad: false);
        }

        OptimizerStep = Math.Max(0, state.OptimizerStep);
        RebindParameterFields();
    }

    private Tensor TensorFrom(float[] values, long[] shape, bool requiresGrad)
    {
        var tensor = torch.tensor(values, dtype: ScalarType.Float32, device: device).reshape(shape).detach();
        return requiresGrad ? tensor.requires_grad_(true) : tensor;
    }

    private static float[] ToArray(Tensor tensor) =>
        tensor.detach().cpu().reshape([-1]).data<float>().ToArray();

    private Tensor FindParameter(string name) =>
        parameters.First(parameter => parameter.Name == name).Value;

    private static Tensor RmsNorm(Tensor x, Tensor weight)
    {
        var variance = x.pow(2).mean([-1], keepdim: true);
        return x * torch.rsqrt(variance + RmsEpsilon) * weight;
    }

    private static Tensor ApplyDropout(Tensor x, double dropout, bool training)
    {
        if (!training || dropout <= 0)
        {
            return x;
        }

        return torch.nn.functional.dropout(x, p: dropout, training: true);
    }

    private static Tensor ApplyRoPE(Tensor tensor, Device device)
    {
        var shape = tensor.shape;
        var sequenceLength = shape[2];
        var headDim = shape[3];
        var halfDim = headDim / 2;
        var positions = torch.arange(sequenceLength, dtype: ScalarType.Float32, device: device).unsqueeze(1);
        var dims = torch.arange(halfDim, dtype: ScalarType.Float32, device: device).unsqueeze(0);
        var invFreq = torch.pow(10000.0, -2.0 * dims / headDim);
        var angles = positions * invFreq;
        var cos = angles.cos().unsqueeze(0).unsqueeze(0);
        var sin = angles.sin().unsqueeze(0).unsqueeze(0);
        var reshaped = tensor.reshape([shape[0], shape[1], sequenceLength, halfDim, 2]);
        var even = reshaped.select(-1, 0);
        var odd = reshaped.select(-1, 1);
        var rotatedEven = even * cos - odd * sin;
        var rotatedOdd = even * sin + odd * cos;
        return torch.stack([rotatedEven, rotatedOdd], dim: -1).reshape(shape);
    }

    private static Device ResolveDevice(string configured)
    {
        if (configured.Equals("cuda", StringComparison.OrdinalIgnoreCase) && torch.cuda.is_available())
        {
            return torch.CUDA;
        }

        return torch.CPU;
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(TorchTransformerLanguageModel));
        }
    }

    private sealed class ParameterState(string name, Tensor value) : IDisposable
    {
        public string Name { get; } = name;

        public Tensor Value { get; set; } = value;

        public Tensor FirstMoment { get; set; } = torch.zeros_like(value);

        public Tensor SecondMoment { get; set; } = torch.zeros_like(value);

        public void Dispose()
        {
            Value.Dispose();
            FirstMoment.Dispose();
            SecondMoment.Dispose();
        }
    }

    private sealed class TorchTransformerLayer
    {
        private readonly int index;

        public TorchTransformerLayer(
            int index,
            TorchTransformerConfig config,
            Func<string, long[], double, bool, Tensor> createParameter)
        {
            this.index = index;
            Norm1 = createParameter(Name("norm1"), [config.Width], 1.0, false);
            Wq = createParameter(Name("wq"), [config.Width, config.Width], 0.02, true);
            Wk = createParameter(Name("wk"), [config.Width, config.Width], 0.02, true);
            Wv = createParameter(Name("wv"), [config.Width, config.Width], 0.02, true);
            Wo = createParameter(Name("wo"), [config.Width, config.Width], 0.02, true);
            Norm2 = createParameter(Name("norm2"), [config.Width], 1.0, false);
            WGate = createParameter(Name("w_gate"), [config.Width, config.FeedForwardSize], 0.02, true);
            WUp = createParameter(Name("w_up"), [config.Width, config.FeedForwardSize], 0.02, true);
            WDown = createParameter(Name("w_down"), [config.FeedForwardSize, config.Width], 0.02, true);
        }

        public Tensor Norm1 { get; private set; }

        public Tensor Wq { get; private set; }

        public Tensor Wk { get; private set; }

        public Tensor Wv { get; private set; }

        public Tensor Wo { get; private set; }

        public Tensor Norm2 { get; private set; }

        public Tensor WGate { get; private set; }

        public Tensor WUp { get; private set; }

        public Tensor WDown { get; private set; }

        public Tensor Forward(Tensor input, TorchTransformerConfig config, bool training, Device device)
        {
            var attentionInput = RmsNorm(input, Norm1);
            var attention = Attention(attentionInput, config, training, device);
            var afterAttention = input + ApplyDropout(attention, config.Dropout, training);
            var ffnInput = RmsNorm(afterAttention, Norm2);
            var hidden = torch.nn.functional.silu(torch.matmul(ffnInput, WGate)) * torch.matmul(ffnInput, WUp);
            var ffn = torch.matmul(hidden, WDown);
            return afterAttention + ApplyDropout(ffn, config.Dropout, training);
        }

        public void Rebind(IReadOnlyList<ParameterState> parameters)
        {
            Norm1 = Find(parameters, Name("norm1"));
            Wq = Find(parameters, Name("wq"));
            Wk = Find(parameters, Name("wk"));
            Wv = Find(parameters, Name("wv"));
            Wo = Find(parameters, Name("wo"));
            Norm2 = Find(parameters, Name("norm2"));
            WGate = Find(parameters, Name("w_gate"));
            WUp = Find(parameters, Name("w_up"));
            WDown = Find(parameters, Name("w_down"));
        }

        private Tensor Attention(Tensor input, TorchTransformerConfig config, bool training, Device device)
        {
            var batch = input.shape[0];
            var sequence = input.shape[1];
            var headDim = config.Width / config.HeadCount;
            var q = torch.matmul(input, Wq).reshape([batch, sequence, config.HeadCount, headDim]).transpose(1, 2);
            var k = torch.matmul(input, Wk).reshape([batch, sequence, config.HeadCount, headDim]).transpose(1, 2);
            var v = torch.matmul(input, Wv).reshape([batch, sequence, config.HeadCount, headDim]).transpose(1, 2);
            q = ApplyRoPE(q, device);
            k = ApplyRoPE(k, device);
            var scores = torch.matmul(q, k.transpose(-2, -1)) / Math.Sqrt(headDim);
            var mask = torch.ones([sequence, sequence], dtype: ScalarType.Bool, device: device).triu(1);
            scores = scores.masked_fill(mask.unsqueeze(0).unsqueeze(0), -1e9);
            var weights = scores.softmax(-1);
            weights = ApplyDropout(weights, config.Dropout, training);
            var context = torch.matmul(weights, v)
                .transpose(1, 2)
                .contiguous()
                .reshape([batch, sequence, config.Width]);
            return torch.matmul(context, Wo);
        }

        private string Name(string suffix) => $"layers.{index}.{suffix}";

        private static Tensor Find(IEnumerable<ParameterState> parameters, string name) =>
            parameters.First(parameter => parameter.Name == name).Value;
    }
}
