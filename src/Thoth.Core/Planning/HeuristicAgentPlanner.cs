using System.Text.RegularExpressions;
using Thoth.Core.Tools;

namespace Thoth.Core.Planning;

public sealed class HeuristicAgentPlanner : IAgentPlanner
{
    public Task<AgentPlan> CreatePlanAsync(
        AgentPlanningContext context,
        CancellationToken cancellationToken = default)
    {
        var steps = new List<AgentPlanStep>();
        var goal = context.Request.Goal;

        AddIfAvailable(
            context,
            steps,
            "memory.search",
            "Look for relevant project memory before acting.",
            new Dictionary<string, string?> { ["query"] = goal, ["limit"] = "5" });

        if (LooksLikeRememberRequest(goal))
        {
            AddIfAvailable(
                context,
                steps,
                "memory.write",
                "Persist the explicit user memory request.",
                new Dictionary<string, string?> { ["scope"] = "project", ["content"] = goal });
        }

        AddIfAvailable(
            context,
            steps,
            "workspace.map",
            "Map the workspace so the agent understands the project shape.",
            new Dictionary<string, string?> { ["maxDepth"] = "4" });

        var path = ExtractLikelyPath(goal);
        if (!string.IsNullOrWhiteSpace(path))
        {
            AddIfAvailable(
                context,
                steps,
                "file.read",
                "Read the file mentioned in the goal.",
                new Dictionary<string, string?> { ["path"] = path, ["maxChars"] = "20000" });
        }

        var query = ExtractSearchQuery(goal);
        if (!string.IsNullOrWhiteSpace(query))
        {
            AddIfAvailable(
                context,
                steps,
                "file.search",
                "Search the workspace for goal-relevant text.",
                new Dictionary<string, string?> { ["query"] = query, ["maxResults"] = "25" });
        }

        return Task.FromResult(new AgentPlan(
            "Heuristic bootstrap plan using memory, workspace mapping, and targeted search.",
            steps,
            "heuristic"));
    }

    private static void AddIfAvailable(
        AgentPlanningContext context,
        ICollection<AgentPlanStep> steps,
        string toolName,
        string thought,
        IReadOnlyDictionary<string, string?> arguments)
    {
        if (context.Tools.Any(tool => string.Equals(tool.Name, toolName, StringComparison.OrdinalIgnoreCase)))
        {
            steps.Add(new AgentPlanStep(thought, new ToolInvocation(toolName, arguments)));
        }
    }

    private static bool LooksLikeRememberRequest(string goal) =>
        goal.Contains("remember", StringComparison.OrdinalIgnoreCase) ||
        goal.Contains("store", StringComparison.OrdinalIgnoreCase) ||
        goal.Contains("save this", StringComparison.OrdinalIgnoreCase);

    private static string ExtractLikelyPath(string goal)
    {
        var match = Regex.Match(goal, @"(?<path>[\w./\\-]+\.(cs|json|md|txt|sln|csproj|yml|yaml|xml))", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["path"].Value : string.Empty;
    }

    private static string ExtractSearchQuery(string goal)
    {
        var words = Regex.Matches(goal, @"[\p{L}\p{N}_-]{3,}")
            .Select(match => match.Value)
            .Where(word => !StopWords.Contains(word.ToLowerInvariant()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

        return string.Join(' ', words);
    }

    private static readonly HashSet<string> StopWords =
    [
        "the",
        "and",
        "for",
        "with",
        "from",
        "this",
        "that",
        "build",
        "make",
        "please"
    ];
}
