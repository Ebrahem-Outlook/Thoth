using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Thoth.Core.Agent;
using Thoth.Core.Configuration;
using Thoth.Core.Memory;
using Thoth.Core.Tools;
using Thoth.Runtime;

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
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Configuration
            .AddJsonFile(Path.Combine(Environment.CurrentDirectory, "configs", "thoth.json"), optional: true)
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

    private static int RunTools(IServiceProvider services, string[] args)
    {
        if (args.Length == 0 || args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            var tools = services.GetRequiredService<IToolRegistry>().List();
            foreach (var tool in tools)
            {
                Console.WriteLine($"{tool.Name}");
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
                var content = parsed.RemainingText;
                if (string.IsNullOrWhiteSpace(content))
                {
                    Console.Error.WriteLine("Missing memory content.");
                    return 2;
                }

                var scope = parsed.GetValue("--scope") ?? "project";
                var record = await memory.AddAsync(scope, content, cancellationToken: cancellationToken);
                Console.WriteLine($"Stored {record.Id:N} in {record.Scope}.");
                return 0;
            }

            case "search":
            {
                var query = parsed.RemainingText;
                var records = await memory.SearchAsync(
                    query,
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

    private static string Indent(string value, string prefix)
    {
        return string.Join(Environment.NewLine, value.Split('\n').Select(line => prefix + line.TrimEnd('\r')));
    }

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
        Thoth AI Agent

        Commands:
          thoth run "goal" [--workspace path] [--max-steps n] [--model name] [--dry-run]
          thoth chat
          thoth tools list
          thoth memory add "note" [--scope project]
          thoth memory search "query" [--scope project] [--limit n]
          thoth memory recent [--scope project] [--limit n]
          thoth config show

        Examples:
          thoth run "summarize this workspace"
          thoth memory add "User prefers Arabic progress updates" --scope user
        """);
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

                if (token.Equals("--dry-run", StringComparison.OrdinalIgnoreCase))
                {
                    flags.Add(token);
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

        public int GetInt(string name, int defaultValue)
        {
            return int.TryParse(GetValue(name), out var value) ? value : defaultValue;
        }
    }
}
