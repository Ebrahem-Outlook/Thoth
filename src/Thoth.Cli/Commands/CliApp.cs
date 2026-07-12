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
using Thoth.Evaluation;
using Thoth.Inference;
using Thoth.Model;
using Thoth.Model.Persistence;
using Thoth.Runtime;
using Thoth.Tokenization;
using Thoth.Training;

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

    private static async Task<int> RunTrainAsync(
        IServiceProvider services,
        string[] args,
        CancellationToken cancellationToken)
    {
        var parsed = ParsedArguments.Parse(args);
        var appOptions = services.GetRequiredService<IOptions<ThothOptions>>().Value;
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
        var tokenizer = services.GetRequiredService<ITextTokenizer>();
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

        var model = await ModelCheckpoint.LoadAsync(checkpoint, cancellationToken);
        var text = new NeuralTextGenerator(model, tokenizer).Generate(
            prompt,
            new GenerationOptions
            {
                MaxNewTokens = parsed.GetInt("--tokens", appOptions.Model.MaxNewTokens),
                Temperature = parsed.GetDouble("--temperature", appOptions.Model.Temperature),
                TopK = parsed.GetInt("--top-k", appOptions.Model.TopK),
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
        var tokenizer = services.GetRequiredService<ITextTokenizer>();
        var dataPath = parsed.GetValue("--data") ??
                       (!string.IsNullOrWhiteSpace(parsed.RemainingText)
                           ? parsed.RemainingText
                           : Path.Combine(appOptions.DataDirectory, "training", "validation"));
        var checkpoint = Path.GetFullPath(parsed.GetValue("--checkpoint") ?? appOptions.Model.CheckpointPath);
        var model = await ModelCheckpoint.LoadAsync(checkpoint, cancellationToken);
        var corpus = await CorpusLoader.LoadCorpusAsync(dataPath, tokenizer, cancellationToken: cancellationToken);
        var report = LanguageModelEvaluator.Evaluate(
            model,
            corpus.Tokens,
            parsed.TryGetInt("--sequence"),
            parsed.GetInt("--max-sequences", 1000));
        var reportPath = Path.GetFullPath(parsed.GetValue("--report") ?? checkpoint + ".evaluation.json");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        await File.WriteAllTextAsync(
            reportPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }),
            cancellationToken);

        var metrics = new CheckpointEvaluationMetrics(
            report.EvaluatedTokens,
            report.EvaluatedSequences,
            report.AverageLoss,
            report.Perplexity,
            report.Scores ?? new Dictionary<string, double>());
        var currentMetadata = await ModelCheckpointQualityGate.LoadMetadataAsync(checkpoint, cancellationToken);
        await ModelCheckpointQualityGate.SaveMetadataAsync(
            checkpoint,
            ModelCheckpointMetadata.CreateUnqualified(
                model,
                currentMetadata?.DatasetManifestPath,
                reportPath,
                metrics),
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
          thoth train --data path [--checkpoint path] [--epochs n] [--steps-per-epoch n]
                      [--sequence n] [--embedding n] [--hidden n] [--lr value] [--fresh]
          thoth generate "prompt" [--checkpoint path] [--tokens n] [--temperature value] [--top-k n] [--experimental]
          thoth evaluate [--data path] [--checkpoint path] [--sequence n] [--report path]
          thoth model-status [--checkpoint path]

        Utilities:
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
            options.Model.Quality.MinimumAgentDecisionScore);

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
