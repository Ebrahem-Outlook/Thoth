using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Thoth.Core.Chat;

namespace Thoth.Llm.Models;

public sealed class LocalReasoningChatModel : IChatModel
{
    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var transcript = string.Join("\n", request.Messages.Select(message => message.Content));
        var content = transcript.Contains("Return a JSON plan only", StringComparison.OrdinalIgnoreCase)
            ? CreatePlan(transcript)
            : CreateFinalAnswer(transcript);

        return Task.FromResult(new ChatResponse(content, "thoth-local"));
    }

    private static string CreatePlan(string transcript)
    {
        var goal = ExtractLineAfterLabel(transcript, "Goal:");
        var steps = new List<Dictionary<string, object?>>
        {
            Step("Look for relevant memory first.", "memory.search", new Dictionary<string, string?>
            {
                ["query"] = goal,
                ["limit"] = "5"
            }),
            Step("Map the workspace to understand project structure.", "workspace.map", new Dictionary<string, string?>
            {
                ["maxDepth"] = "4"
            })
        };

        var path = ExtractLikelyPath(goal);
        if (!string.IsNullOrWhiteSpace(path))
        {
            steps.Add(Step("Read the file mentioned in the goal.", "file.read", new Dictionary<string, string?>
            {
                ["path"] = path,
                ["maxChars"] = "20000"
            }));
        }

        var query = ExtractSearchQuery(goal);
        if (!string.IsNullOrWhiteSpace(query))
        {
            steps.Add(Step("Search the workspace for related code and text.", "file.search", new Dictionary<string, string?>
            {
                ["query"] = query,
                ["maxResults"] = "25"
            }));
        }

        var payload = new Dictionary<string, object?>
        {
            ["summary"] = "Local bootstrap plan.",
            ["steps"] = steps
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string CreateFinalAnswer(string transcript)
    {
        var goal = ExtractAfterLabel(transcript, "Goal:");
        var observations = ExtractAfterLabel(transcript, "Observations:");
        var builder = new StringBuilder();

        builder.AppendLine("Thoth run completed.");
        if (!string.IsNullOrWhiteSpace(goal))
        {
            builder.AppendLine($"Goal: {SingleLine(goal, 220)}");
        }

        builder.AppendLine();
        builder.AppendLine("What I inspected:");

        var toolLines = Regex.Matches(observations, @"Tool:\s*(?<tool>[\w.]+)")
            .Select(match => match.Groups["tool"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (toolLines.Length == 0)
        {
            builder.AppendLine("- No tools were needed.");
        }
        else
        {
            foreach (var tool in toolLines)
            {
                builder.AppendLine($"- {tool}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Key observation:");
        builder.AppendLine(SummarizeObservation(observations));

        builder.AppendLine();
        builder.AppendLine("Next move: continue by giving Thoth a concrete build, edit, or analysis goal.");
        return builder.ToString().Trim();
    }

    private static Dictionary<string, object?> Step(
        string thought,
        string tool,
        Dictionary<string, string?> arguments)
    {
        return new Dictionary<string, object?>
        {
            ["thought"] = thought,
            ["tool"] = tool,
            ["arguments"] = arguments
        };
    }

    private static string ExtractAfterLabel(string text, string label)
    {
        var index = text.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return string.Empty;
        }

        var valueStart = index + label.Length;
        var nextBlankLine = text.IndexOf("\n\n", valueStart, StringComparison.Ordinal);
        return nextBlankLine < 0
            ? text[valueStart..].Trim()
            : text[valueStart..nextBlankLine].Trim();
    }

    private static string ExtractLineAfterLabel(string text, string label)
    {
        var index = text.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return string.Empty;
        }

        var valueStart = index + label.Length;
        var lineEnd = text.IndexOf('\n', valueStart);
        return lineEnd < 0
            ? text[valueStart..].Trim()
            : text[valueStart..lineEnd].Trim();
    }

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

    private static string SummarizeObservation(string observations)
    {
        var lines = observations
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line =>
                !line.StartsWith("Step ", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("Tool:", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("Succeeded:", StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .ToArray();

        return lines.Length == 0
            ? "No detailed observations were produced."
            : "- " + string.Join("\n- ", lines.Select(line => SingleLine(line, 180)));
    }

    private static string SingleLine(string value, int maxLength)
    {
        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= maxLength ? singleLine : singleLine[..maxLength] + "...";
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
        "please",
        "workspace",
        "project"
    ];
}
