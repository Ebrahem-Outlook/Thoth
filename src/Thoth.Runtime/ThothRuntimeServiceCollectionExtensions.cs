using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Thoth.Cognition.Concepts;
using Thoth.Cognition.Procedures;
using Thoth.Cognition.Tasks;
using Thoth.Core.Agent;
using Thoth.Core.Chat;
using Thoth.Core.Configuration;
using Thoth.Core.Conversations;
using Thoth.Core.Memory;
using Thoth.Core.Planning;
using Thoth.Core.Sandbox;
using Thoth.Core.Tools;
using Thoth.Core.Understanding;
using Thoth.Inference;
using Thoth.Llm.Models;
using Thoth.Memory.Cognition;
using Thoth.Memory.Conversations;
using Thoth.Memory.Sqlite;
using Thoth.Model.Persistence;
using Thoth.Sandbox.Policies;
using Thoth.Tokenization;
using Thoth.Tools;

namespace Thoth.Runtime;

public static class ThothRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddThothRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ThothOptions>(configuration.GetSection("Thoth"));
        services.PostConfigure<ThothOptions>(options =>
        {
            options.WorkspaceRoot = ResolvePath(options.WorkspaceRoot);
            options.DataDirectory = ResolvePath(options.DataDirectory);
            options.Model.CheckpointPath = ResolvePath(options.Model.CheckpointPath);
            if (!string.IsNullOrWhiteSpace(options.Model.TokenizerPath) &&
                !options.Model.TokenizerPath.Equals("byte", StringComparison.OrdinalIgnoreCase))
            {
                options.Model.TokenizerPath = ResolvePath(options.Model.TokenizerPath);
            }

            options.Sandbox.AllowedShellExecutables = options.Sandbox.AllowedShellExecutables
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            options.Sandbox.BlockedCommandTokens = options.Sandbox.BlockedCommandTokens
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        });

        services.AddSingleton<ITextTokenizer>(provider =>
            CreateTokenizer(provider.GetRequiredService<IOptions<ThothOptions>>().Value));

        services.AddSingleton<IMemoryStore>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ThothOptions>>().Value;
            return new SqliteMemoryStore(Path.Combine(options.DataDirectory, "memory", "thoth.sqlite"));
        });

        services.AddSingleton<IConversationStore>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ThothOptions>>().Value;
            return new SqliteConversationStore(Path.Combine(options.DataDirectory, "memory", "thoth.sqlite"));
        });

        services.AddSingleton<IConversationTaskStore>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ThothOptions>>().Value;
            return new SqliteConversationTaskStore(Path.Combine(options.DataDirectory, "memory", "thoth.sqlite"));
        });

        services.AddSingleton<IConceptGraphStore>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ThothOptions>>().Value;
            return new SqliteConceptGraphStore(Path.Combine(options.DataDirectory, "memory", "thoth.sqlite"));
        });

        services.AddSingleton<CodeTaskExtractor>();
        services.AddSingleton<TaskContinuationResolver>();
        services.AddSingleton<TaskMerger>();
        services.AddSingleton<ProcedureRegistry>();
        services.AddSingleton<ConceptActivationService>();

        services.AddSingleton<IExecutionPolicy>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ThothOptions>>().Value;
            return new LocalExecutionPolicy(options.Sandbox);
        });

        services.AddSingleton<IToolRegistry>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ThothOptions>>().Value;
            return DefaultToolSet.Create(TimeSpan.FromSeconds(Math.Max(options.Sandbox.ShellTimeoutSeconds, 1)));
        });

        services.AddSingleton<IChatModel>(provider => CreateChatModel(
            provider.GetRequiredService<IOptions<ThothOptions>>().Value,
            provider.GetRequiredService<ITextTokenizer>()));

        services.AddSingleton<IAgentDecisionService>(provider =>
            new ModelAgentDecisionService(
                provider.GetRequiredService<IChatModel>(),
                new HeuristicAgentDecisionService()));

        // Kept for compatibility with existing integrations. AgentEngine now
        // uses the iterative decision service instead of a static plan.
        services.AddSingleton<IAgentPlanner>(provider =>
            new JsonAgentPlanner(provider.GetRequiredService<IChatModel>(), new HeuristicAgentPlanner()));

        services.AddSingleton<IUserUnderstandingService>(provider =>
            new SelfUnderstandingService(
                provider.GetRequiredService<IChatModel>(),
                new HeuristicUnderstandingService()));

        services.AddSingleton<AgentEngine>();
        services.AddSingleton<ChatOrchestrator>();

        return services;
    }

    private static IChatModel CreateChatModel(ThothOptions options, ITextTokenizer tokenizer)
    {
        var provider = options.Model.Provider.Trim().ToLowerInvariant();
        var fallback = new SelfContainedReasoningModel();
        if (provider is "self" or "fallback")
        {
            return fallback;
        }

        var thresholds = ToThresholds(options.Model.Quality);
        var inspection = ModelCheckpointQualityGate
            .InspectAsync(options.Model.CheckpointPath, thresholds)
            .GetAwaiter()
            .GetResult();

        if (inspection.Status == ModelCheckpointStatus.Missing)
        {
            if (provider == "neural")
            {
                throw new FileNotFoundException(
                    "The neural provider was selected but no checkpoint exists. Train one with `thoth train` first.",
                    options.Model.CheckpointPath);
            }

            return fallback;
        }

        if (!inspection.CanUse(ModelRole.Generation))
        {
            if (provider == "neural")
            {
                throw new InvalidOperationException(
                    $"The selected checkpoint is not qualified for generation: {string.Join("; ", inspection.Reasons)}");
            }

            return fallback;
        }

        IChatModel neural;
        try
        {
            neural = CreateQualifiedNeuralModel(options, tokenizer, inspection);
        }
        catch (Exception exception) when (provider != "neural" && exception is not OperationCanceledException)
        {
            return fallback;
        }

        return new QualityGatedChatModel(neural, fallback, inspection);
    }

    private static IChatModel CreateQualifiedNeuralModel(
        ThothOptions options,
        ITextTokenizer tokenizer,
        ModelCheckpointInspection inspection)
    {
        if (inspection.Metadata is null)
        {
            throw new InvalidOperationException("Qualified checkpoint metadata is missing.");
        }

        if (!IsTokenizerCompatible(inspection.Metadata.Tokenizer, tokenizer))
        {
            throw new InvalidOperationException(
                $"Runtime tokenizer cannot serve checkpoint tokenizer {inspection.Metadata.Tokenizer}.");
        }

        var generationOptions = new GenerationOptions
        {
            MaxNewTokens = options.Model.MaxNewTokens,
            Temperature = options.Model.Temperature,
            TopK = options.Model.TopK,
            Seed = options.Model.Seed
        };

        return inspection.Metadata.Architecture switch
        {
            ModelCheckpointMetadata.LegacyRecurrentArchitecture => new NeuralChatModel(
                ModelCheckpoint.LoadAsync(options.Model.CheckpointPath).GetAwaiter().GetResult(),
                tokenizer,
                generationOptions),
            ModelCheckpointMetadata.TransformerArchitecture => new TransformerChatModel(
                TransformerCheckpoint.LoadAsync(options.Model.CheckpointPath).GetAwaiter().GetResult(),
                tokenizer,
                generationOptions),
            ModelCheckpointMetadata.TorchTransformerArchitecture => new TorchTransformerChatModel(
                TorchTransformerCheckpoint.LoadAsync(options.Model.CheckpointPath).GetAwaiter().GetResult(),
                tokenizer,
                generationOptions),
            _ => throw new InvalidOperationException($"Unsupported checkpoint architecture {inspection.Metadata.Architecture}.")
        };
    }

    private static ITextTokenizer CreateTokenizer(ThothOptions options)
    {
        var tokenizerPath = options.Model.TokenizerPath;
        if (string.IsNullOrWhiteSpace(tokenizerPath) ||
            tokenizerPath.Equals("byte", StringComparison.OrdinalIgnoreCase))
        {
            return new ByteTokenizer();
        }

        return BpeTokenizer.LoadAsync(tokenizerPath).GetAwaiter().GetResult();
    }

    private static bool IsTokenizerCompatible(string expectedTokenizer, ITextTokenizer tokenizer) =>
        expectedTokenizer switch
        {
            ModelCheckpointMetadata.ByteTokenizer => tokenizer is ByteTokenizer,
            ModelCheckpointMetadata.BpeTokenizer => tokenizer is BpeTokenizer,
            _ => false
        };

    private static CheckpointQualityThresholds ToThresholds(CheckpointQualityOptions options) =>
        new(
            options.MinimumOptimizerSteps,
            options.MinimumEvaluatedTokens,
            options.MaximumAverageLoss,
            options.MaximumPerplexity,
            options.MinimumGenerationHealthScore,
            options.MinimumUnderstandingScore,
            options.MinimumAgentDecisionScore,
            options.MinimumLanguageHealthScore,
            options.MinimumLeakageScore,
            options.MinimumDeterministicLoadingScore,
            options.MinimumTaskBenchmarkScore);

    private static string ResolvePath(string path)
    {
        return Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(ThothPathDiscovery.FindWorkspaceRoot(Environment.CurrentDirectory), path));
    }

    private sealed class QualityGatedChatModel(
        IChatModel neural,
        IChatModel fallback,
        ModelCheckpointInspection inspection) : IChatModel
    {
        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            var role = request.Purpose switch
            {
                ModelRequestPurpose.UnderstandUser => ModelRole.Understanding,
                ModelRequestPurpose.AgentDecision or ModelRequestPurpose.AgentPlan => ModelRole.AgentDecision,
                _ => ModelRole.Generation
            };

            if (!inspection.CanUse(role))
            {
                return fallback.CompleteAsync(request, cancellationToken);
            }

            return CompleteSafelyAsync(request, cancellationToken);
        }

        private async Task<ChatResponse> CompleteSafelyAsync(
            ChatRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                var response = await neural.CompleteAsync(request, cancellationToken);
                return ModelOutputSafety.IsUsable(response.Content)
                    ? response
                    : await fallback.CompleteAsync(request, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return await fallback.CompleteAsync(request, cancellationToken);
            }
        }
    }

    private static class ModelOutputSafety
    {
        private static readonly string[] InternalMarkers =
        [
            "ordered tasks",
            "request.atomize",
            "language.prepare",
            "contract.design",
            "answer.revise",
            "internal critique",
            "executed observations",
            "stop reason:",
            "cognitive frame",
            "route:",
            "intent:",
            "terms:"
        ];

        public static bool IsUsable(string? content)
        {
            if (string.IsNullOrWhiteSpace(content) || content.Contains('\uFFFD'))
            {
                return false;
            }

            if (!HasValidUtf16(content))
            {
                return false;
            }

            if (InternalMarkers.Any(marker => content.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return !IsDegenerate(content);
        }

        private static bool HasValidUtf16(string value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                var current = value[index];
                if (char.IsHighSurrogate(current))
                {
                    if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                    {
                        return false;
                    }

                    index++;
                    continue;
                }

                if (char.IsLowSurrogate(current))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsDegenerate(string output)
        {
            var nonWhite = output.Where(ch => !char.IsWhiteSpace(ch)).ToArray();
            if (nonWhite.Length >= 24)
            {
                var mostCommon = nonWhite
                    .GroupBy(ch => ch)
                    .Max(group => group.Count());
                if (mostCommon / (double)nonWhite.Length > 0.72)
                {
                    return true;
                }
            }

            var words = output
                .Split([' ', '\r', '\n', '\t'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return words.Length >= 18 &&
                   words.GroupBy(word => word, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() / (double)words.Length > 0.55);
        }
    }
}
