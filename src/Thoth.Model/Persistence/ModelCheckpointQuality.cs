using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thoth.Model.Persistence;

public enum ModelCheckpointStatus
{
    Missing,
    LoadingFailed,
    Unqualified,
    ExperimentalOnly,
    QualifiedForGeneration,
    QualifiedForUnderstanding,
    QualifiedForAgentDecisions
}

public enum ModelRole
{
    Generation,
    Understanding,
    AgentDecision
}

public sealed record CheckpointQualityThresholds(
    long MinimumOptimizerSteps = 1,
    int MinimumEvaluatedTokens = 32,
    double MaximumAverageLoss = 8.0,
    double MaximumPerplexity = 3_000.0,
    double MinimumGenerationHealthScore = 0.80,
    double MinimumUnderstandingScore = 0.90,
    double MinimumAgentDecisionScore = 0.90,
    double MinimumLanguageHealthScore = 0.80,
    double MinimumLeakageScore = 1.0,
    double MinimumDeterministicLoadingScore = 1.0,
    double MinimumTaskBenchmarkScore = 0.70);

public sealed record CheckpointEvaluationMetrics(
    int EvaluatedTokens,
    int EvaluatedSequences,
    double AverageLoss,
    double Perplexity,
    IReadOnlyDictionary<string, double> Scores);

public sealed record ModelCheckpointMetadata(
    int MetadataVersion,
    string CheckpointFormat,
    int CheckpointFormatVersion,
    string Architecture,
    string Tokenizer,
    string ModelConfigHash,
    long OptimizerStep,
    DateTimeOffset CreatedAt,
    string? DatasetManifestPath,
    string? EvaluationReportPath,
    CheckpointEvaluationMetrics? Metrics)
{
    public const int CurrentMetadataVersion = 1;
    public const string LegacyRecurrentArchitecture = "legacy-recurrent-rnn-v1";
    public const string TransformerArchitecture = "decoder-only-transformer-v1";
    public const string ByteTokenizer = "byte-v1";
    public const string BpeTokenizer = "bpe-v1";
    public const string CurrentArchitecture = LegacyRecurrentArchitecture;
    public const string CurrentTokenizer = ByteTokenizer;

    public static ModelCheckpointMetadata CreateUnqualified(
        RecurrentLanguageModel model,
        string? datasetManifestPath = null,
        string? evaluationReportPath = null,
        CheckpointEvaluationMetrics? metrics = null) =>
        new(
            CurrentMetadataVersion,
            ModelCheckpoint.CurrentFormat,
            ModelCheckpoint.CurrentFormatVersion,
            LegacyRecurrentArchitecture,
            ByteTokenizer,
            ModelCheckpointQualityGate.HashConfig(model.Config),
            model.OptimizerStep,
            DateTimeOffset.UtcNow,
            datasetManifestPath,
            evaluationReportPath,
            metrics);

    public static ModelCheckpointMetadata CreateUnqualified(
        TransformerLanguageModel model,
        string? datasetManifestPath = null,
        string? evaluationReportPath = null,
        CheckpointEvaluationMetrics? metrics = null,
        string tokenizer = BpeTokenizer) =>
        new(
            CurrentMetadataVersion,
            TransformerCheckpoint.CurrentFormat,
            TransformerCheckpoint.CurrentFormatVersion,
            TransformerArchitecture,
            tokenizer,
            ModelCheckpointQualityGate.HashConfig(model.Config),
            model.OptimizerStep,
            DateTimeOffset.UtcNow,
            datasetManifestPath,
            evaluationReportPath,
            metrics);
}

public sealed record ModelCheckpointInspection(
    ModelCheckpointStatus Status,
    string CheckpointPath,
    string MetadataPath,
    ModelCheckpointMetadata? Metadata,
    IReadOnlyList<string> Reasons)
{
    public bool CanUse(ModelRole role) =>
        role switch
        {
            ModelRole.Generation => Status is ModelCheckpointStatus.QualifiedForGeneration or
                ModelCheckpointStatus.QualifiedForUnderstanding or
                ModelCheckpointStatus.QualifiedForAgentDecisions,
            ModelRole.Understanding => Status is ModelCheckpointStatus.QualifiedForUnderstanding or
                ModelCheckpointStatus.QualifiedForAgentDecisions,
            ModelRole.AgentDecision => Status == ModelCheckpointStatus.QualifiedForAgentDecisions,
            _ => false
        };
}

public static class ModelCheckpointQualityGate
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string MetadataPath(string checkpointPath) =>
        Path.GetFullPath(checkpointPath) + ".metadata.json";

    public static async Task<ModelCheckpointInspection> InspectAsync(
        string checkpointPath,
        CheckpointQualityThresholds? thresholds = null,
        CancellationToken cancellationToken = default)
    {
        thresholds ??= new CheckpointQualityThresholds();
        var fullPath = Path.GetFullPath(checkpointPath);
        var metadataPath = MetadataPath(fullPath);
        var reasons = new List<string>();

        if (!File.Exists(fullPath))
        {
            return new ModelCheckpointInspection(
                ModelCheckpointStatus.Missing,
                fullPath,
                metadataPath,
                null,
                ["checkpoint file is missing"]);
        }

        CheckpointIdentity identity;
        try
        {
            identity = await LoadIdentityAsync(fullPath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
        {
            return new ModelCheckpointInspection(
                ModelCheckpointStatus.LoadingFailed,
                fullPath,
                metadataPath,
                null,
                [$"checkpoint load failed: {exception.Message}"]);
        }

        var metadata = await LoadMetadataAsync(fullPath, cancellationToken);
        if (metadata is null)
        {
            return new ModelCheckpointInspection(
                ModelCheckpointStatus.Unqualified,
                fullPath,
                metadataPath,
                null,
                ["checkpoint metadata is missing"]);
        }

        ValidateMetadata(metadata, identity, thresholds, reasons);
        var status = reasons.Count == 0
            ? DetermineQualifiedStatus(metadata, thresholds, reasons)
            : ModelCheckpointStatus.Unqualified;

        return new ModelCheckpointInspection(
            status,
            fullPath,
            MetadataPath(fullPath),
            metadata,
            status is ModelCheckpointStatus.Unqualified or ModelCheckpointStatus.ExperimentalOnly ? reasons.ToArray() : []);
    }

    public static async Task<ModelCheckpointMetadata?> LoadMetadataAsync(
        string checkpointPath,
        CancellationToken cancellationToken = default)
    {
        var metadataPath = MetadataPath(checkpointPath);
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        await using var stream = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<ModelCheckpointMetadata>(stream, JsonOptions, cancellationToken);
    }

    public static async Task SaveMetadataAsync(
        string checkpointPath,
        ModelCheckpointMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var metadataPath = MetadataPath(checkpointPath);
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        var temporaryPath = metadataPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, metadata, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, metadataPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static string HashConfig(NeuralModelConfig config)
    {
        var value = $"{config.VocabularySize}:{config.EmbeddingSize}:{config.HiddenSize}:{config.SequenceLength}:{config.Seed}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    public static string HashConfig(TransformerConfig config)
    {
        var value = string.Join(
            ':',
            config.VocabularySize,
            config.ContextLength,
            config.LayerCount,
            config.Width,
            config.HeadCount,
            config.FeedForwardSize,
            config.Dropout.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            config.Seed,
            config.TieOutputEmbeddings);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static async Task<CheckpointIdentity> LoadIdentityAsync(
        string checkpointPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var recurrent = await ModelCheckpoint.LoadAsync(checkpointPath, cancellationToken);
            return new CheckpointIdentity(
                ModelCheckpoint.CurrentFormat,
                ModelCheckpoint.CurrentFormatVersion,
                ModelCheckpointMetadata.LegacyRecurrentArchitecture,
                ModelCheckpointMetadata.ByteTokenizer,
                HashConfig(recurrent.Config),
                recurrent.OptimizerStep);
        }
        catch (InvalidDataException recurrentException)
        {
            try
            {
                var transformer = await TransformerCheckpoint.LoadAsync(checkpointPath, cancellationToken);
                return new CheckpointIdentity(
                    TransformerCheckpoint.CurrentFormat,
                    TransformerCheckpoint.CurrentFormatVersion,
                    ModelCheckpointMetadata.TransformerArchitecture,
                    null,
                    HashConfig(transformer.Config),
                    transformer.OptimizerStep);
            }
            catch (InvalidDataException transformerException)
            {
                throw new InvalidDataException(
                    $"Unsupported checkpoint format. Legacy loader: {recurrentException.Message}; Transformer loader: {transformerException.Message}",
                    transformerException);
            }
        }
    }

    private static void ValidateMetadata(
        ModelCheckpointMetadata metadata,
        CheckpointIdentity identity,
        CheckpointQualityThresholds thresholds,
        List<string> reasons)
    {
        if (metadata.MetadataVersion != ModelCheckpointMetadata.CurrentMetadataVersion)
        {
            reasons.Add($"metadata version {metadata.MetadataVersion} is not supported");
        }

        if (metadata.CheckpointFormat != identity.CheckpointFormat ||
            metadata.CheckpointFormatVersion != identity.CheckpointFormatVersion)
        {
            reasons.Add("checkpoint format metadata is incompatible");
        }

        if (metadata.Architecture != identity.Architecture)
        {
            reasons.Add($"architecture {metadata.Architecture} is not qualified");
        }

        if (identity.RequiredTokenizer is not null && metadata.Tokenizer != identity.RequiredTokenizer)
        {
            reasons.Add($"tokenizer {metadata.Tokenizer} is not compatible");
        }

        if (metadata.Architecture == ModelCheckpointMetadata.TransformerArchitecture &&
            metadata.Tokenizer is not ModelCheckpointMetadata.ByteTokenizer and not ModelCheckpointMetadata.BpeTokenizer)
        {
            reasons.Add($"tokenizer {metadata.Tokenizer} is not compatible");
        }

        if (metadata.ModelConfigHash != identity.ModelConfigHash)
        {
            reasons.Add("model config hash does not match checkpoint weights");
        }

        if (metadata.OptimizerStep < thresholds.MinimumOptimizerSteps)
        {
            reasons.Add($"optimizer step {metadata.OptimizerStep} is below required {thresholds.MinimumOptimizerSteps}");
        }

    }

    private static ModelCheckpointStatus DetermineQualifiedStatus(
        ModelCheckpointMetadata metadata,
        CheckpointQualityThresholds thresholds,
        List<string> reasons)
    {
        var metrics = metadata.Metrics;
        if (metrics is null)
        {
            reasons.Add("mandatory evaluation metrics are missing; checkpoint remains experimental only");
            return ModelCheckpointStatus.ExperimentalOnly;
        }

        if (metrics.EvaluatedTokens < thresholds.MinimumEvaluatedTokens)
        {
            reasons.Add($"evaluated token count {metrics.EvaluatedTokens} is below required {thresholds.MinimumEvaluatedTokens}");
        }

        if (!double.IsFinite(metrics.AverageLoss) || metrics.AverageLoss > thresholds.MaximumAverageLoss)
        {
            reasons.Add($"average loss {metrics.AverageLoss:0.###} exceeds maximum {thresholds.MaximumAverageLoss:0.###}");
        }

        if (!double.IsFinite(metrics.Perplexity) || metrics.Perplexity > thresholds.MaximumPerplexity)
        {
            reasons.Add($"perplexity {metrics.Perplexity:0.###} exceeds maximum {thresholds.MaximumPerplexity:0.###}");
        }

        if (reasons.Count > 0)
        {
            return ModelCheckpointStatus.Unqualified;
        }

        var generation = Score(metrics, "generation_health") >= thresholds.MinimumGenerationHealthScore &&
                         Score(metrics, "language_health") >= thresholds.MinimumLanguageHealthScore &&
                         Score(metrics, "no_internal_leak") >= thresholds.MinimumLeakageScore &&
                         Score(metrics, "deterministic_loading") >= thresholds.MinimumDeterministicLoadingScore &&
                         Score(metrics, "minimum_task_benchmarks") >= thresholds.MinimumTaskBenchmarkScore;
        var understanding = Score(metrics, "language_detection") >= thresholds.MinimumUnderstandingScore &&
                            Score(metrics, "tool_routing") >= thresholds.MinimumUnderstandingScore;
        var agent = understanding &&
                    Score(metrics, "structured_agent_decision") >= thresholds.MinimumAgentDecisionScore;

        if (agent)
        {
            return ModelCheckpointStatus.QualifiedForAgentDecisions;
        }

        if (understanding)
        {
            return ModelCheckpointStatus.QualifiedForUnderstanding;
        }

        if (generation)
        {
            return ModelCheckpointStatus.QualifiedForGeneration;
        }

        reasons.Add("role-specific benchmark scores did not meet thresholds");
        return ModelCheckpointStatus.Unqualified;
    }

    private static double Score(CheckpointEvaluationMetrics metrics, string name) =>
        metrics.Scores.TryGetValue(name, out var value) && double.IsFinite(value) ? value : 0.0;

    private sealed record CheckpointIdentity(
        string CheckpointFormat,
        int CheckpointFormatVersion,
        string Architecture,
        string? RequiredTokenizer,
        string ModelConfigHash,
        long OptimizerStep);
}
