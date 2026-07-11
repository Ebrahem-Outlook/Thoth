using System.Text.RegularExpressions;
using Thoth.Core.Tools;

namespace Thoth.Core.Agent;

/// <summary>
/// Conservative fallback used before the neural checkpoint has learned reliable
/// structured tool use. It never writes files and adapts to search observations.
/// </summary>
public sealed class HeuristicAgentDecisionService : IAgentDecisionService
{
    public Task<AgentDecision> DecideAsync(
        AgentDecisionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var usedTools = context.Steps
            .Where(step => step.Invocation is not null)
            .Select(step => step.Invocation!.ToolName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (HasTool(context, "workspace.summary") && !usedTools.Contains("workspace.summary"))
        {
            return Use("Inspect the workspace shape before selecting files.", "workspace.summary");
        }

        if (HasTool(context, "workspace.map") && !usedTools.Contains("workspace.map"))
        {
            return Use("Build a compact map of the workspace after the initial summary.", "workspace.map", new()
            {
                ["maxDepth"] = "4",
                ["maxEntries"] = "250"
            });
        }

        var explicitPath = ExtractPath(context.Request.Goal);
        if (!string.IsNullOrWhiteSpace(explicitPath) &&
            HasTool(context, "file.read") &&
            !WasRead(context, explicitPath))
        {
            return Use("Read the file explicitly named by the user.", "file.read", new()
            {
                ["path"] = explicitPath,
                ["maxChars"] = "50000"
            });
        }

        var searchStep = context.Steps.LastOrDefault(step =>
            string.Equals(step.Invocation?.ToolName, "file.search", StringComparison.OrdinalIgnoreCase) &&
            step.Result?.Succeeded == true);
        var discoveredPath = searchStep is null ? null : ExtractFirstSearchPath(searchStep.Result!.Content);
        if (!string.IsNullOrWhiteSpace(discoveredPath) &&
            HasTool(context, "file.read") &&
            !WasRead(context, discoveredPath))
        {
            return Use("Read the strongest file discovered by the previous search.", "file.read", new()
            {
                ["path"] = discoveredPath,
                ["maxChars"] = "50000"
            });
        }

        if (HasTool(context, "file.search") && !usedTools.Contains("file.search"))
        {
            return Use("Search for the most distinctive goal term before drawing a conclusion.", "file.search", new()
            {
                ["query"] = ExtractSearchTerm(context.Request.Goal),
                ["maxResults"] = "40"
            });
        }

        return Task.FromResult(AgentDecision.Finish(string.Empty, "The fallback inspection sequence is complete; synthesize from collected evidence."));
    }

    private static Task<AgentDecision> Use(
        string rationale,
        string tool,
        Dictionary<string, string?>? arguments = null) =>
        Task.FromResult(AgentDecision.UseTool(
            rationale,
            new ToolInvocation(tool, arguments ?? new Dictionary<string, string?>())));

    private static bool HasTool(AgentDecisionContext context, string name) =>
        context.Tools.Any(tool => string.Equals(tool.Name, name, StringComparison.OrdinalIgnoreCase));

    private static bool WasRead(AgentDecisionContext context, string path) =>
        context.Steps.Any(step =>
            step.Invocation is not null &&
            string.Equals(step.Invocation.ToolName, "file.read", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(step.Invocation.GetString("path"), path, StringComparison.OrdinalIgnoreCase));

    private static string? ExtractPath(string goal)
    {
        var match = Regex.Match(
            goal,
            @"(?<path>(?:[\w.-]+[\\/])*[\w.-]+\.(?:cs|csproj|sln|ts|tsx|js|html|scss|css|json|md|sql|py|java|go|rs))",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["path"].Value.Replace('\\', '/') : null;
    }

    private static string? ExtractFirstSearchPath(string content)
    {
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf(':');
            var candidate = separator > 1 ? line[..separator] : line;
            if (Path.HasExtension(candidate) && !candidate.Contains(" file name match", StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Replace('\\', '/');
            }
        }

        return null;
    }

    private static string ExtractSearchTerm(string goal)
    {
        var words = Regex.Matches(goal, @"[\p{L}\p{N}_-]{3,}")
            .Select(match => match.Value)
            .Where(word => !StopWords.Contains(word))
            .OrderByDescending(word => word.Length)
            .ToArray();
        return words.FirstOrDefault() ?? goal.Trim();
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "this", "that", "with", "from", "project", "workspace", "file", "please",
        "\u0639\u0627\u064a\u0632", "\u0627\u0644\u0645\u0634\u0631\u0648\u0639", "\u0627\u0644\u0645\u0644\u0641", "\u0627\u0639\u0645\u0644", "\u0628\u0635", "\u0639\u0644\u0649", "\u0645\u0646", "\u0641\u064a"
    };
}
