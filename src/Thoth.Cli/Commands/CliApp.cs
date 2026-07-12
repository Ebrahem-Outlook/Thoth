using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Thoth.Core.Agent;
using Thoth.Core.Configuration;
using Thoth.Core.Memory;
using Thoth.Core.Tools;
using Thoth.Data.Acquisition;
using Thoth.Data.Manifests;
using Thoth.Data.Synthetic;
using Thoth.Evaluation;
using Thoth.Inference;
using Thoth.Model;
using Thoth.Model.Persistence;
using Thoth.Runtime;
using Thoth.Tokenization;
using Thoth.Training;
using Thoth.Training.Hardware;

namespace Thoth.Cli.Commands;

public static class CliApp
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        using var host = BuildHost(args);
        await host.StartAsync(cancellationToken);

        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteHelp();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        try
        {
            return command switch
            {
                "run" => await RunAgentAsync(host.Services, rest, cancellationToken),
                "chat" => await RunChatAsync(host.Services, cancellationToken),
                "train" => await RunTrainAsync(host.Services, rest, cancellationToken),
                "generate" => await RunGenerateAsync(host.Services, rest, cancellationToken),
                "evaluate" => await RunEvaluateAsync(host.Services, rest, cancellationToken),
                "model-status" => await RunModelStatusAsync(host.Services, rest, cancellationToken),
                "tokenizer" => await RunTokenizerAsync(host.Services, rest, cancellationToken),
                "hardware" => RunHardware(host.Services, rest),
                "data" => await RunDataAsync(host.Services, rest, cancellationToken),
                "tools" => RunTools(host.Services, rest),
                "memory" => await RunMemoryAsync(host.Services, rest, cancellationToken),
                "config" => RunConfig(host.Services, rest),
                _ => UnknownCommand(command)
            };
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    private static IHost BuildHost(string[] args)
    {
        var workspaceRoot = ThothPathDiscovery.FindWorkspaceRoot(Environment.CurrentDirectory);
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Configuration
            .AddJsonFile(Path.Combine(workspaceRoot, "configs", "thoth.json"), optional: true)
            .AddEnvironmentVariables("THOTH_");

        builder.Services.AddThothRuntime(builder.Configuration);
        return builder.Build();
    }

    private static async Task<int> RunAgentAsync(
        IServiceProvider services,
        string[] args,
        CancellationToken cancellationToken)
    {
        var parsed = ParsedArguments.Parse(args);
        var goal = parsed.RemainingText;
        if (string.IsNullOrWhiteSpace(goal))
        {
            Console.Error.WriteLine("Missing goal. Example: thoth run \"summarize this workspace\"");
            return 2;
        }

        var options = services.GetRequiredService<IOptions<ThothOptions>>().Value;
        var workingDirectory = parsed.GetValue("--workspace") ?? options.WorkspaceRoot;
        var model = parsed.GetValue("--model") ?? options.Model.Model;
        var dryRun = parsed.HasFlag("--dry-run");
        var maxSteps = parsed.GetInt("--max-steps", options.MaxAgentSteps);

        var engine = services.GetRequiredService<AgentEngine>();
        var run = await engine.RunAsync(
            new AgentRequest(goal, workingDirectory, model, maxSteps, dryRun),
            cancellationToken);

        WriteRun(run);
        return run.Succeeded ? 0 : 1;
    }

    private static async Task<int> RunChatAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var options = services.GetRequiredService<IOptions<ThothOptions>>().Value;
        var engine = services.GetRequiredService<AgentEngine>();

        Console.WriteLine("Thoth chat. Type 'exit' to leave.");
        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("> ");
            var input = Console.ReadLine();
            if (input is null || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            var run = await engine.RunAsync(
                new AgentRequest(input, options.WorkspaceRoot, options.Model.Model, options.MaxAgentSteps),
                cancellationToken);

            Console.WriteLine();
            Console.WriteLine(run.FinalAnswer);
            Console.WriteLine();
        }

        return 0;
    }

    private static async Task<int> RunTokenizerAsync(
        IServiceProvider services,
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            Console.WriteLine("Usage: thoth tokenizer train --data path --output data/tokenizers/thoth-bpe [--vocab-size 8000]");
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        if (command != "train")
        {
            Console.Error.WriteLine("Unknown tokenizer command. Try: thoth tokenizer train --data path --output path");
            return 2;
        }

        var parsed = ParsedArguments.Parse(args.Skip(1).ToArray());
        var appOptions = services.GetRequiredService<IOptions<ThothOptions>>().Value;
        var dataPath = parsed.GetValue("--data") ??
                       (!string.IsNullOrWhiteSpace(parsed.RemainingText)
                           ? parsed.RemainingText
                           : Path.Combine(appOptions.DataDirectory, "training"));
        var output = parsed.GetValue("--output") ?? Path.Combine(appOptions.DataDirectory, "tokenizers", "thoth-bpe");
        var profileName = parsed.GetValue("--profile");
        var profile = profileName is null ? BpeTokenizerProfiles.LaptopPilot : BpeTokenizerProfiles.Resolve(profileName);
        var vocabularySize = parsed.GetInt("--vocab-size", profile.VocabularySize);
        var options = new BpeTokenizerTrainingOptions(
            vocabularySize,
            parsed.GetInt("--min-frequency", 2),
            parsed.HasFlag("--normalize-nfc"),
            profile.Name);

        var tokenizer = await BpeTokenizer.TrainFromFilesAsync(dataPath, options, cancellationToken);
        await tokenizer.SaveAsync(output, cancellationToken);
        Console.WriteLine($"Tokenizer: {Path.GetFullPath(output)}");
        Console.WriteLine($"Profile: {options.Profile}");
        Console.WriteLine($"Vocabulary: {tokenizer.VocabularySize:n0}");
        Console.WriteLine($"Merges: {tokenizer.Merges.Count:n0}");
        Console.WriteLine($"Training manifest: {tokenizer.TrainingManifestSha256}");
        return 0;
    }

    private static async Task<int> RunTrainAsync(
        IServiceProvider services,
        string[] args,
        CancellationToken cancellationToken)
    {
        var parsed = ParsedArguments.Parse(args);
        var appOptions = services.GetRequiredService<IOptions<ThothOptions>>().Value;
        if (IsTransformer(parsed))
        {
            return await RunTransformerTrainAsync(appOptions, parsed, cancellationToken);
        }

        var tokenizer = services.GetRequiredService<ITextTokenizer>();
        var dataPath = parsed.GetValue("--data") ??
                       (!string.IsNullOrWhiteSpace(parsed.RemainingText)
                           ? parsed.RemainingText
                           : Path.Combine(appOptions.DataDirectory, "training"));

        var checkpointPath = Path.GetFullPath(parsed.GetValue("--checkpoint") ?? appOptions.Model.CheckpointPath);
        RecurrentLanguageModel model;
        if (File.Exists(checkpointPath) && !parsed.HasFlag("--fresh"))
        {
            model = await ModelCheckpoint.LoadAsync(checkpointPath, cancellationToken);
            Console.WriteLine($"Resuming checkpoint at step {model.OptimizerStep:n0}: {checkpointPath}");
        }
        else
        {
            var modelConfig = new NeuralModelConfig(
                tokenizer.VocabularySize,
                parsed.GetInt("--embedding", appOptions.Model.EmbeddingSize),
                parsed.GetInt("--hidden", appOptions.Model.HiddenSize),
                parsed.GetInt("--model-sequence", appOptions.Model.SequenceLength),
                parsed.GetInt("--seed", appOptions.Model.Seed));
            model = new RecurrentLanguageModel(modelConfig);
            Console.WriteLine($"Created random model with {model.ParameterCount:n0} trainable parameters.");
        }

        var corpus = await CorpusLoader.LoadCorpusAsync(dataPath, tokenizer, cancellationToken: cancellationToken);
        Console.WriteLine($"Loaded {corpus.Tokens.Length:n0} corpus tokens from {corpus.Manifest.FileCount:n0} files.");

        var options = new TrainingOptions
        {
            Epochs = parsed.GetInt("--epochs", 3),
            StepsPerEpoch = parsed.TryGetInt("--steps-per-epoch"),
            SequenceLength = parsed.GetInt("--sequence", Math.Min(128, model.Config.SequenceLength)),
            LearningRate = parsed.GetDouble("--lr", 0.001),
            MinimumLearningRate = parsed.GetDouble("--min-lr", 0.00005),
            WarmupSteps = parsed.GetInt("--warmup", 100),
            WeightDecay = parsed.GetDouble("--weight-decay", 0.01),
            GradientClip = parsed.GetDouble("--gradient-clip", 1.0),
            CheckpointEverySteps = parsed.GetInt("--checkpoint-every", 500),
            Seed = parsed.GetInt("--seed", appOptions.Model.Seed)
        };

        var progress = new Progress<TrainingProgress>(value =>
        {
            if (value.StepInEpoch == 1 || value.StepInEpoch % 10 == 0 || value.StepInEpoch == value.StepsPerEpoch)
            {
                Console.WriteLine(
                    $"epoch {value.Epoch}/{value.Epochs} step {value.StepInEpoch}/{value.StepsPerEpoch} " +
                    $"global {value.GlobalStep:n0} loss {value.Loss:F4} ema {value.SmoothedLoss:F4} lr {value.LearningRate:E2}");
            }
        });

        var report = await new LanguageModelTrainer(model, tokenizer)
            .TrainAsync(corpus.Tokens, options, checkpointPath, progress, cancellationToken);
        var manifestPath = checkpointPath + ".dataset-manifest.json";
        await CorpusLoader.WriteManifestAsync(manifestPath, corpus.Manifest, cancellationToken);
        await ModelCheckpointQualityGate.SaveMetadataAsync(
            checkpointPath,
            ModelCheckpointMetadata.CreateUnqualified(model, manifestPath),
            cancellationToken);

        Console.WriteLine();
        Console.WriteLine($"Checkpoint: {report.CheckpointPath}");
        Console.WriteLine($"Dataset manifest: {manifestPath}");
        Console.WriteLine($"Steps: {report.StartingStep:n0} -> {report.CompletedStep:n0}");
        Console.WriteLine($"Loss: {report.InitialLoss:F4} -> {report.FinalLoss:F4}");
        Console.WriteLine($"Tokens seen: {report.TokensSeen:n0}");
        Console.WriteLine($"Elapsed: {report.Elapsed}");
        return 0;
    }

    private static async Task<int> RunTransformerTrainAsync(
        ThothOptions appOptions,
        ParsedArguments parsed,
        CancellationToken cancellationToken)
    {
        var (tokenizer, tokenizerName) = await ResolveTokenizerAsync(appOptions, parsed, cancellationToken);
        var dataPath = parsed.GetValue("--data") ??
                       (!string.IsNullOrWhiteSpace(parsed.RemainingText)
                           ? parsed.RemainingText
                           : Path.Combine(appOptions.DataDirectory, "training"));
        var checkpointPath = Path.GetFullPath(parsed.GetValue("--checkpoint") ??
                                              Path.Combine(appOptions.DataDirectory, "models", "thoth-transformer.bin"));

        TransformerLanguageModel model;
        if (File.Exists(checkpointPath) && !parsed.HasFlag("--fresh"))
        {
            model = await TransformerCheckpoint.LoadAsync(checkpointPath, cancellationToken);
            Console.WriteLine($"Resuming Transformer checkpoint at step {model.OptimizerStep:n0}: {checkpointPath}");
        }
        else
        {
            var preset = parsed.GetValue("--preset") ?? "tiny";
            var config = preset.Equals("bootstrap", StringComparison.OrdinalIgnoreCase)
                ? TransformerConfig.Bootstrap(tokenizer.VocabularySize, parsed.GetInt("--seed", appOptions.Model.Seed))
                : TransformerConfig.Tiny(tokenizer.VocabularySize, parsed.GetInt("--seed", appOptions.Model.Seed));

            config = config with
            {
                ContextLength = parsed.GetInt("--context", config.ContextLength),
                LayerCount = parsed.GetInt("--layers", config.LayerCount),
                Width = parsed.GetInt("--width", config.Width),
                HeadCount = parsed.GetInt("--heads", config.HeadCount),
                FeedForwardSize = parsed.GetInt("--ffn", config.FeedForwardSize),
                Dropout = parsed.GetDouble("--dropout", config.Dropout)
            };
            model = new TransformerLanguageModel(config);
            Console.WriteLine($"Created random Transformer with {model.ParameterCount:n0} parameters.");
        }

        var corpus = await CorpusLoader.LoadCorpusAsync(dataPath, tokenizer, cancellationToken: cancellationToken);
        Console.WriteLine($"Loaded {corpus.Tokens.Length:n0} corpus tokens from {corpus.Manifest.FileCount:n0} files.");

        var options = new TrainingOptions
        {
            Epochs = parsed.GetInt("--epochs", 3),
            StepsPerEpoch = parsed.TryGetInt("--steps-per-epoch"),
            SequenceLength = parsed.GetInt("--sequence", Math.Min(128, model.ContextLength)),
            BatchSize = parsed.GetInt("--batch-size", 1),
            LearningRate = parsed.GetDouble("--lr", 0.001),
            MinimumLearningRate = parsed.GetDouble("--min-lr", 0.00005),
            WarmupSteps = parsed.GetInt("--warmup", 100),
            WeightDecay = parsed.GetDouble("--weight-decay", 0.01),
            GradientClip = parsed.GetDouble("--gradient-clip", 1.0),
            CheckpointEverySteps = parsed.GetInt("--checkpoint-every", 500),
            Seed = parsed.GetInt("--seed", appOptions.Model.Seed)
        };

        var progress = new Progress<TrainingProgress>(value =>
        {
            if (value.StepInEpoch == 1 || value.StepInEpoch % 10 == 0 || value.StepInEpoch == value.StepsPerEpoch)
            {
                Console.WriteLine(
                    $"transformer epoch {value.Epoch}/{value.Epochs} step {value.StepInEpoch}/{value.StepsPerEpoch} " +
                    $"global {value.GlobalStep:n0} loss {value.Loss:F4} ema {value.SmoothedLoss:F4} lr {value.LearningRate:E2}");
            }
        });

        var report = await new TransformerLanguageModelTrainer(model)
            .TrainAsync(corpus.Tokens, options, checkpointPath, tokenizerName, progress, cancellationToken);
        var manifestPath = checkpointPath + ".dataset-manifest.json";
        await CorpusLoader.WriteManifestAsync(manifestPath, corpus.Manifest, cancellationToken);
        await ModelCheckpointQualityGate.SaveMetadataAsync(
            checkpointPath,
            ModelCheckpointMetadata.CreateUnqualified(model, manifestPath, tokenizer: tokenizerName),
            cancellationToken);

        Console.WriteLine();
        Console.WriteLine($"Transformer checkpoint: {report.CheckpointPath}");
        Console.WriteLine($"Tokenizer: {tokenizerName}");
        Console.WriteLine($"Dataset manifest: {manifestPath}");
        Console.WriteLine($"Steps: {report.StartingStep:n0} -> {report.CompletedStep:n0}");
        Console.WriteLine($"Loss: {report.InitialLoss:F4} -> {report.FinalLoss:F4}");
        Console.WriteLine($"Tokens seen: {report.TokensSeen:n0}");
        Console.WriteLine($"Elapsed: {report.Elapsed}");
        return 0;
    }

    private static async Task<int> RunGenerateAsync(
        IServiceProvider services,
        string[] args,
        CancellationToken cancellationToken)
    {
        var parsed = ParsedArguments.Parse(args);
        var prompt = parsed.RemainingText;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Console.Error.WriteLine("Missing prompt. Example: thoth generate \"User: hello\\nAssistant:\"");
            return 2;
        }

        var appOptions = services.GetRequiredService<IOptions<ThothOptions>>().Value;
        var checkpoint = Path.GetFullPath(parsed.GetValue("--checkpoint") ?? appOptions.Model.CheckpointPath);
        var inspection = await ModelCheckpointQualityGate.InspectAsync(checkpoint, ToThresholds(appOptions), cancellationToken);
        if (!inspection.CanUse(ModelRole.Generation) && !parsed.HasFlag("--experimental"))
        {
            Console.Error.WriteLine($"Checkpoint is not qualified for generation: {inspection.Status}");
            foreach (var reason in inspection.Reasons)
            {
                Console.Error.WriteLine($"- {reason}");
            }

            Console.Error.WriteLine("Use --experimental to run raw unqualified generation deliberately.");
            return 1;
        }

        if (IsTransformer(parsed))
        {
            var (transformerTokenizer, _) = await ResolveTokenizerAsync(appOptions, parsed, cancellationToken);
            var transformer = await TransformerCheckpoint.LoadAsync(checkpoint, cancellationToken);
            var generated = new TransformerTextGenerator(transformer, transformerTokenizer).Generate(
                prompt,
                new GenerationOptions
                {
                    MaxNewTokens = parsed.GetInt("--tokens", appOptions.Model.MaxNewTokens),
                    Temperature = parsed.GetDouble("--temperature", appOptions.Model.Temperature),
                    TopK = parsed.GetInt("--top-k", appOptions.Model.TopK),
                    TopP = parsed.GetDouble("--top-p", 1.0),
                    Seed = parsed.TryGetInt("--seed")
                });
            Console.WriteLine(generated);
            return 0;
        }

        var tokenizer = services.GetRequiredService<ITextTokenizer>();
        var model = await ModelCheckpoint.LoadAsync(checkpoint, cancellationToken);
        var text = new NeuralTextGenerator(model, tokenizer).Generate(
            prompt,
            new GenerationOptions
            {
                MaxNewTokens = parsed.GetInt("--tokens", appOptions.Model.MaxNewTokens),
                Temperature = parsed.GetDouble("--temperature", appOptions.Model.Temperature),
                TopK = parsed.GetInt("--top-k", appOptions.Model.TopK),
                TopP = parsed.GetDouble("--top-p", 1.0),
                Seed = parsed.TryGetInt("--seed")
            });
        Console.WriteLine(text);
        return 0;
    }

    private static async Task<int> RunEvaluateAsync(
        IServiceProvider services,
        string[] args,
        CancellationToken cancellationToken)
    {
        var parsed = ParsedArguments.Parse(args);
        var appOptions = services.GetRequiredService<IOptions<ThothOptions>>().Value;
        var (tokenizer, tokenizerName) = IsTransformer(parsed)
            ? await ResolveTokenizerAsync(appOptions, parsed, cancellationToken)
            : (services.GetRequiredService<ITextTokenizer>(), ModelCheckpointMetadata.ByteTokenizer);
        var dataPath = parsed.GetValue("--data") ??
                       (!string.IsNullOrWhiteSpace(parsed.RemainingText)
                           ? parsed.RemainingText
                           : Path.Combine(appOptions.DataDirectory, "training", "validation"));
        var checkpoint = Path.GetFullPath(parsed.GetValue("--checkpoint") ?? appOptions.Model.CheckpointPath);
        var corpus = await CorpusLoader.LoadCorpusAsync(dataPath, tokenizer, cancellationToken: cancellationToken);
        EvaluationReport report;
        ModelCheckpointMetadata metadata;
        if (IsTransformer(parsed))
        {
            var transformer = await TransformerCheckpoint.LoadAsync(checkpoint, cancellationToken);
            report = LanguageModelEvaluator.Evaluate(
                transformer,
                corpus.Tokens,
                parsed.TryGetInt("--sequence"),
                parsed.GetInt("--max-sequences", 1000));
            var current = await ModelCheckpointQualityGate.LoadMetadataAsync(checkpoint, cancellationToken);
            metadata = ModelCheckpointMetadata.CreateUnqualified(
                transformer,
                current?.DatasetManifestPath,
                metrics: ToMetrics(report),
                tokenizer: tokenizerName);
        }
        else
        {
            var model = await ModelCheckpoint.LoadAsync(checkpoint, cancellationToken);
            report = LanguageModelEvaluator.Evaluate(
                model,
                corpus.Tokens,
                parsed.TryGetInt("--sequence"),
                parsed.GetInt("--max-sequences", 1000));
            var current = await ModelCheckpointQualityGate.LoadMetadataAsync(checkpoint, cancellationToken);
            metadata = ModelCheckpointMetadata.CreateUnqualified(
                model,
                current?.DatasetManifestPath,
                metrics: ToMetrics(report));
        }

        var reportPath = Path.GetFullPath(parsed.GetValue("--report") ?? checkpoint + ".evaluation.json");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        await File.WriteAllTextAsync(
            reportPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }),
            cancellationToken);

        metadata = metadata with { EvaluationReportPath = reportPath };
        await ModelCheckpointQualityGate.SaveMetadataAsync(
            checkpoint,
            metadata,
            cancellationToken);

        Console.WriteLine($"Evaluated tokens: {report.EvaluatedTokens:n0}");
        Console.WriteLine($"Sequences: {report.EvaluatedSequences:n0}");
        Console.WriteLine($"Average loss: {report.AverageLoss:F6}");
        Console.WriteLine($"Perplexity: {report.Perplexity:F3}");
        Console.WriteLine($"Report: {reportPath}");
        return 0;
    }

    private static async Task<int> RunModelStatusAsync(
        IServiceProvider services,
        string[] args,
        CancellationToken cancellationToken)
    {
        var parsed = ParsedArguments.Parse(args);
        var appOptions = services.GetRequiredService<IOptions<ThothOptions>>().Value;
        var checkpoint = Path.GetFullPath(parsed.GetValue("--checkpoint") ?? appOptions.Model.CheckpointPath);
        var inspection = await ModelCheckpointQualityGate.InspectAsync(checkpoint, ToThresholds(appOptions), cancellationToken);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        Console.WriteLine(JsonSerializer.Serialize(inspection, options));
        return inspection.Status is ModelCheckpointStatus.LoadingFailed ? 1 : 0;
    }

    private static int RunTools(IServiceProvider services, string[] args)
    {
        if (args.Length == 0 || args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var tool in services.GetRequiredService<IToolRegistry>().List())
            {
                Console.WriteLine(tool.Name);
                Console.WriteLine($"  {tool.Description}");
                foreach (var parameter in tool.Parameters)
                {
                    Console.WriteLine($"  - {parameter.Name} ({parameter.Type}) {(parameter.Required ? "required" : "optional")}: {parameter.Description}");
                }
            }

            return 0;
        }

        Console.Error.WriteLine("Unknown tools command. Try: thoth tools list");
        return 2;
    }

    private static int RunHardware(IServiceProvider services, string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            Console.WriteLine("Usage: thoth hardware inspect [--json] [--training-dir path] [--checkpoint-dir path] [--tokenizer-dir path]");
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        if (command != "inspect")
        {
            Console.Error.WriteLine("Unknown hardware command. Try: thoth hardware inspect");
            return 2;
        }

        var parsed = ParsedArguments.Parse(args.Skip(1).ToArray());
        var appOptions = services.GetRequiredService<IOptions<ThothOptions>>().Value;
        var directories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["training"] = parsed.GetValue("--training-dir") ?? Path.Combine(appOptions.DataDirectory, "training"),
            ["checkpoints"] = parsed.GetValue("--checkpoint-dir") ?? Path.Combine(appOptions.DataDirectory, "models"),
            ["tokenizers"] = parsed.GetValue("--tokenizer-dir") ?? Path.Combine(appOptions.DataDirectory, "tokenizers")
        };

        var profile = LocalHardwareProbe.Inspect(directories);
        if (parsed.HasFlag("--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(
                profile,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            return profile.Torch.CpuBackendAvailable &&
                   profile.WritableDirectories.All(directory => directory.Writable)
                ? 0
                : 1;
        }

        Console.WriteLine("Thoth local hardware profile");
        Console.WriteLine($"OS: {profile.OperatingSystem} ({profile.Architecture})");
        Console.WriteLine($"CPU: {profile.CpuName ?? "unknown"}");
        Console.WriteLine($"Cores: physical {profile.PhysicalCpuCores?.ToString() ?? "unknown"}, logical {profile.LogicalCpuCores}");
        Console.WriteLine($"Recommended Torch CPU threads: {profile.RecommendedTorchCpuThreads}");
        Console.WriteLine($"RAM: total {FormatBytes(profile.TotalRamBytes)}, available {FormatBytes(profile.AvailableRamBytes)}");
        Console.WriteLine($"Torch CPU: {(profile.Torch.CpuBackendAvailable ? "available" : "unavailable")}");
        Console.WriteLine($"CUDA: {(profile.Torch.CudaAvailable ? "available" : "not available")}");
        Console.WriteLine($"Device default: {profile.Torch.Device}");
        Console.WriteLine("Dtypes:");
        foreach (var dtype in profile.Torch.DtypeChecks)
        {
            Console.WriteLine($"  {dtype.Key}: {(dtype.Value ? "ok" : "failed")}");
        }

        if (!string.IsNullOrWhiteSpace(profile.Torch.Error))
        {
            Console.WriteLine($"Torch error: {profile.Torch.Error}");
        }

        Console.WriteLine("Disk:");
        foreach (var disk in profile.Disks)
        {
            Console.WriteLine($"  {disk.Root}: free {FormatBytes(disk.FreeBytes)} / total {FormatBytes(disk.TotalBytes)}");
        }

        Console.WriteLine("Writable directories:");
        foreach (var directory in profile.WritableDirectories)
        {
            Console.WriteLine($"  {directory.Purpose}: {(directory.Writable ? "ok" : "failed")} {directory.Path}");
            if (!string.IsNullOrWhiteSpace(directory.Error))
            {
                Console.WriteLine($"    {directory.Error}");
            }
        }

        return profile.Torch.CpuBackendAvailable &&
               profile.WritableDirectories.All(directory => directory.Writable)
            ? 0
            : 1;
    }

    private static async Task<int> RunDataAsync(
        IServiceProvider services,
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            Console.WriteLine("Usage: thoth data init-manifests|list-sources|plan-source|generate-owned");
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var parsed = ParsedArguments.Parse(args.Skip(1).ToArray());
        var appOptions = services.GetRequiredService<IOptions<ThothOptions>>().Value;

        if (command == "init-manifests")
        {
            var output = parsed.GetValue("--output") ?? Path.Combine(appOptions.DataDirectory, "manifests");

            await DataManifestWriter.EnsureSkeletonAsync(output, cancellationToken);
            Console.WriteLine($"Data manifests: {Path.GetFullPath(output)}");
            return 0;
        }

        if (command == "list-sources")
        {
            foreach (var source in AcquisitionPlanCatalog.Sources)
            {
                Console.WriteLine($"{source.SourceId}: {source.DisplayName} ({source.LicenseSpdx})");
            }

            return 0;
        }

        if (command == "plan-source")
        {
            var sourceId = parsed.GetValue("--source") ?? parsed.RemainingText;
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                Console.Error.WriteLine("Missing source. Example: thoth data plan-source --source arwiki");
                return 2;
            }

            var source = AcquisitionPlanCatalog.Resolve(sourceId);
            if (parsed.HasFlag("--json"))
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    source,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
                return 0;
            }

            Console.WriteLine($"{source.DisplayName}");
            Console.WriteLine($"Source: {source.OfficialUrl}");
            Console.WriteLine($"License: {source.LicenseSpdx}");
            Console.WriteLine($"Attribution required: {source.AttributionRequired}");
            Console.WriteLine("Required approval facts:");
            foreach (var fact in source.RequiredApprovalFacts)
            {
                Console.WriteLine($"  - {fact}");
            }

            Console.WriteLine("Steps:");
            foreach (var step in source.Steps.OrderBy(step => step.Order))
            {
                Console.WriteLine($"  {step.Order}. {step.Name}: {step.Description}");
            }

            return 0;
        }

        if (command == "generate-owned")
        {
            var output = parsed.GetValue("--output") ??
                         Path.Combine(appOptions.DataDirectory, "splits", "instruction", "train", "owned-synthetic.jsonl");
            var count = parsed.GetInt("--count", 100);
            var seed = parsed.GetInt("--seed", 1337);

            await new OwnedSyntheticInstructionGenerator().WriteJsonlAsync(output, count, seed, cancellationToken);
            Console.WriteLine($"Owned examples: {Path.GetFullPath(output)}");
            Console.WriteLine($"Count: {count:n0}");
            Console.WriteLine($"Seed: {seed}");
            return 0;
        }

        Console.Error.WriteLine("Unknown data command. Try: thoth data init-manifests");
        return 2;
    }

    private static async Task<int> RunMemoryAsync(
        IServiceProvider services,
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Missing memory command. Try: add, search, recent");
            return 2;
        }

        var memory = services.GetRequiredService<IMemoryStore>();
        await memory.EnsureCreatedAsync(cancellationToken);
        var command = args[0].ToLowerInvariant();
        var parsed = ParsedArguments.Parse(args.Skip(1).ToArray());

        switch (command)
        {
            case "add":
            {
                if (string.IsNullOrWhiteSpace(parsed.RemainingText))
                {
                    Console.Error.WriteLine("Missing memory content.");
                    return 2;
                }

                var record = await memory.AddAsync(
                    parsed.GetValue("--scope") ?? "project",
                    parsed.RemainingText,
                    cancellationToken: cancellationToken);
                Console.WriteLine($"Stored {record.Id:N} in {record.Scope}.");
                return 0;
            }
            case "search":
            {
                var records = await memory.SearchAsync(
                    parsed.RemainingText,
                    parsed.GetValue("--scope"),
                    parsed.GetInt("--limit", 8),
                    cancellationToken);
                WriteMemories(records);
                return 0;
            }
            case "recent":
            {
                var records = await memory.RecentAsync(
                    parsed.GetValue("--scope"),
                    parsed.GetInt("--limit", 8),
                    cancellationToken);
                WriteMemories(records);
                return 0;
            }
            default:
                Console.Error.WriteLine("Unknown memory command. Try: add, search, recent");
                return 2;
        }
    }

    private static int RunConfig(IServiceProvider services, string[] args)
    {
        if (args.Length == 0 || args[0].Equals("show", StringComparison.OrdinalIgnoreCase))
        {
            var options = services.GetRequiredService<IOptions<ThothOptions>>().Value;
            Console.WriteLine(JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        Console.Error.WriteLine("Unknown config command. Try: thoth config show");
        return 2;
    }

    private static void WriteRun(AgentRun run)
    {
        Console.WriteLine($"Run: {run.RunId:N}");
        Console.WriteLine($"Succeeded: {run.Succeeded}");
        Console.WriteLine();
        foreach (var step in run.Steps)
        {
            Console.WriteLine($"{step.Index}. {step.Thought}");
            if (step.Invocation is not null)
            {
                Console.WriteLine($"   tool: {step.Invocation.ToolName}");
            }

            if (step.Result is not null)
            {
                Console.WriteLine($"   result: {(step.Result.Succeeded ? "ok" : "failed")}");
                Console.WriteLine(Indent(step.Result.Content, "   "));
            }
        }

        Console.WriteLine();
        Console.WriteLine(run.FinalAnswer);
    }

    private static void WriteMemories(IReadOnlyList<MemoryRecord> records)
    {
        if (records.Count == 0)
        {
            Console.WriteLine("No memory records found.");
            return;
        }

        foreach (var record in records)
        {
            Console.WriteLine($"{record.Id:N} [{record.CreatedAt:u}] {record.Scope}");
            Console.WriteLine(Indent(record.Content, "  "));
        }
    }

    private static string Indent(string value, string prefix) =>
        string.Join(Environment.NewLine, value.Split('\n').Select(line => prefix + line.TrimEnd('\r')));

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        WriteHelp();
        return 2;
    }

    private static bool IsHelp(string value) =>
        value.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("help", StringComparison.OrdinalIgnoreCase);

    private static void WriteHelp()
    {
        Console.WriteLine("""
        Thoth local neural agent

        Agent:
          thoth run "goal" [--workspace path] [--max-steps n] [--model name] [--dry-run]
          thoth chat

        Neural model:
          thoth hardware inspect [--json] [--training-dir path] [--checkpoint-dir path] [--tokenizer-dir path]
          thoth tokenizer train --data data/training/pretrain --output data/tokenizers/thoth-bpe
                          [--profile smoke|laptop-pilot|laptop-max] [--vocab-size 8000]
                          [--min-frequency 2] [--normalize-nfc]
          thoth train --data path [--checkpoint path] [--epochs n] [--steps-per-epoch n]
                      [--sequence n] [--embedding n] [--hidden n] [--lr value] [--fresh]
          thoth train --architecture transformer --tokenizer data/tokenizers/thoth-bpe --data data/training/pretrain
                      [--checkpoint data/models/thoth-transformer.bin] [--preset tiny|bootstrap]
                      [--layers n] [--width n] [--heads n] [--ffn n] [--batch-size n]
          thoth generate "prompt" [--architecture transformer] [--tokenizer path] [--checkpoint path]
                         [--tokens n] [--temperature value] [--top-k n] [--top-p value] [--experimental]
          thoth evaluate [--architecture transformer] [--tokenizer path] [--data path] [--checkpoint path] [--sequence n] [--report path]
          thoth model-status [--checkpoint path]

        Utilities:
          thoth data init-manifests [--output data/manifests]
          thoth data list-sources
          thoth data plan-source --source arwiki|simplewiki|mdn-content|oasst1|curated-code|owned-synthetic [--json]
          thoth data generate-owned [--output data/splits/instruction/train/owned-synthetic.jsonl] [--count n] [--seed n]
          thoth tools list
          thoth memory add "note" [--scope project]
          thoth memory search "query" [--scope project] [--limit n]
          thoth memory recent [--scope project] [--limit n]
          thoth config show

        Bootstrap example:
          thoth train --epochs 3 --steps-per-epoch 500
          thoth evaluate --data ./data/training/validation.txt
          thoth generate "User: Explain this code.\nAssistant:"
        """);
    }

    private static CheckpointQualityThresholds ToThresholds(ThothOptions options) =>
        new(
            options.Model.Quality.MinimumOptimizerSteps,
            options.Model.Quality.MinimumEvaluatedTokens,
            options.Model.Quality.MaximumAverageLoss,
            options.Model.Quality.MaximumPerplexity,
            options.Model.Quality.MinimumGenerationHealthScore,
            options.Model.Quality.MinimumUnderstandingScore,
            options.Model.Quality.MinimumAgentDecisionScore,
            options.Model.Quality.MinimumLanguageHealthScore,
            options.Model.Quality.MinimumLeakageScore,
            options.Model.Quality.MinimumDeterministicLoadingScore,
            options.Model.Quality.MinimumTaskBenchmarkScore);

    private static bool IsTransformer(ParsedArguments parsed) =>
        (parsed.GetValue("--architecture") ?? parsed.GetValue("--arch") ?? string.Empty)
        .Equals("transformer", StringComparison.OrdinalIgnoreCase);

    private static async Task<(ITextTokenizer Tokenizer, string TokenizerName)> ResolveTokenizerAsync(
        ThothOptions appOptions,
        ParsedArguments parsed,
        CancellationToken cancellationToken)
    {
        var tokenizerPath = parsed.GetValue("--tokenizer");
        if (string.Equals(tokenizerPath, "byte", StringComparison.OrdinalIgnoreCase))
        {
            return (new ByteTokenizer(), ModelCheckpointMetadata.ByteTokenizer);
        }

        tokenizerPath ??= Path.Combine(appOptions.DataDirectory, "tokenizers", "thoth-bpe");
        var resolvedArtifact = Path.HasExtension(tokenizerPath)
            ? Path.GetFullPath(tokenizerPath)
            : Path.Combine(Path.GetFullPath(tokenizerPath), "tokenizer.json");

        if (File.Exists(resolvedArtifact))
        {
            return (await BpeTokenizer.LoadAsync(tokenizerPath, cancellationToken), ModelCheckpointMetadata.BpeTokenizer);
        }

        if (parsed.GetValue("--tokenizer") is not null)
        {
            throw new FileNotFoundException($"Tokenizer artifact was not found: {resolvedArtifact}", resolvedArtifact);
        }

        return (new ByteTokenizer(), ModelCheckpointMetadata.ByteTokenizer);
    }

    private static CheckpointEvaluationMetrics ToMetrics(EvaluationReport report) =>
        new(
            report.EvaluatedTokens,
            report.EvaluatedSequences,
            report.AverageLoss,
            report.Perplexity,
            report.Scores ?? new Dictionary<string, double>());

    private static string FormatBytes(long? bytes)
    {
        if (bytes is null)
        {
            return "unknown";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes.Value;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private sealed record ParsedArguments(
        IReadOnlyDictionary<string, string?> Options,
        IReadOnlySet<string> Flags,
        string RemainingText)
    {
        public static ParsedArguments Parse(string[] args)
        {
            var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var remaining = new List<string>();

            for (var index = 0; index < args.Length; index++)
            {
                var token = args[index];
                if (!token.StartsWith("--", StringComparison.Ordinal))
                {
                    remaining.Add(token);
                    continue;
                }

                if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    options[token] = args[index + 1];
                    index++;
                }
                else
                {
                    flags.Add(token);
                }
            }

            return new ParsedArguments(options, flags, string.Join(' ', remaining).Trim());
        }

        public bool HasFlag(string name) => Flags.Contains(name);

        public string? GetValue(string name) => Options.TryGetValue(name, out var value) ? value : null;

        public int GetInt(string name, int defaultValue) =>
            int.TryParse(GetValue(name), out var value) ? value : defaultValue;

        public int? TryGetInt(string name) =>
            int.TryParse(GetValue(name), out var value) ? value : null;

        public double GetDouble(string name, double defaultValue) =>
            double.TryParse(GetValue(name), System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value
                : defaultValue;
    }
}
