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
using Thoth.Llm.Models;
using Thoth.Memory.Conversations;
using Thoth.Memory.Sqlite;
using Thoth.Sandbox.Policies;
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
        });

        services.AddSingleton<IMemoryStore>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ThothOptions>>().Value;
            var dataDirectory = ResolvePath(options.DataDirectory);
            return new SqliteMemoryStore(Path.Combine(dataDirectory, "memory", "thoth.sqlite"));
        });

        services.AddSingleton<IConversationStore>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ThothOptions>>().Value;
            var dataDirectory = ResolvePath(options.DataDirectory);
            return new SqliteConversationStore(Path.Combine(dataDirectory, "memory", "thoth.sqlite"));
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

        services.AddSingleton<IChatModel>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ThothOptions>>().Value;
            if (ShouldUseOllama(options.Model.Provider))
            {
                return new OllamaChatModel(
                    new HttpClient(),
                    new OllamaChatModelOptions(
                        options.Model.Endpoint,
                        options.Model.Model,
                        options.Model.Temperature));
            }

            return new LocalReasoningChatModel();
        });

        services.AddSingleton<IAgentPlanner>(provider =>
            new JsonAgentPlanner(provider.GetRequiredService<IChatModel>(), new HeuristicAgentPlanner()));

        services.AddSingleton<IUserUnderstandingService>(provider =>
            new LlmUnderstandingService(
                provider.GetRequiredService<IChatModel>(),
                new HeuristicUnderstandingService()));

        services.AddSingleton<AgentEngine>();
        services.AddSingleton<ChatOrchestrator>();

        return services;
    }

    private static bool ShouldUseOllama(string provider)
    {
        return provider.Equals("ollama", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolvePath(string path)
    {
        return Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(ThothPathDiscovery.FindWorkspaceRoot(Environment.CurrentDirectory), path));
    }
}
