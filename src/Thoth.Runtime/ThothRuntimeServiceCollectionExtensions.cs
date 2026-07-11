using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
            options.Sandbox.AllowedShellExecutables = options.Sandbox.AllowedShellExecutables
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            options.Sandbox.BlockedCommandTokens = options.Sandbox.BlockedCommandTokens
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        });

        services.AddSingleton<ITextTokenizer, ByteTokenizer>();

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

        if (!File.Exists(options.Model.CheckpointPath))
        {
            if (provider == "neural")
            {
                throw new FileNotFoundException(
                    "The neural provider was selected but no checkpoint exists. Train one with `thoth train` first.",
                    options.Model.CheckpointPath);
            }

            return fallback;
        }

        var model = ModelCheckpoint.LoadAsync(options.Model.CheckpointPath).GetAwaiter().GetResult();
        return new NeuralChatModel(
            model,
            tokenizer,
            new GenerationOptions
            {
                MaxNewTokens = options.Model.MaxNewTokens,
                Temperature = options.Model.Temperature,
                TopK = options.Model.TopK,
                Seed = options.Model.Seed
            });
    }

    private static string ResolvePath(string path)
    {
        return Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(ThothPathDiscovery.FindWorkspaceRoot(Environment.CurrentDirectory), path));
    }
}
