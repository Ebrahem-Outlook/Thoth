using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Thoth.Core.Chat;

namespace Thoth.Llm.Models;

public sealed class SelfContainedReasoningModel : IChatModel
{
    private static readonly string[] ArabicWorkspaceTerms =
    [
        "\u0645\u0634\u0631\u0648\u0639",
        "\u0645\u0644\u0641",
        "\u0639\u062f\u0644",
        "\u062d\u0633\u0646",
        "\u0627\u0628\u0646\u064a",
        "\u0646\u0641\u0630",
        "\u0643\u0648\u062f",
        "\u0641\u0631\u0648\u0646\u062a",
        "\u0648\u0627\u062c\u0647\u0629",
        "\u0628\u0627\u0643",
        "\u0627\u0646\u062c\u0644\u0648\u0631"
    ];

    private static readonly string[] BackendTerms =
    [
        "backend",
        "api",
        "endpoint",
        "http",
        "controller",
        "route",
        "server",
        "dotnet",
        "asp.net",
        "\u0628\u0627\u0643"
    ];

    private static readonly string[] FrontendTerms =
    [
        "frontend",
        "ui",
        "angular",
        "component",
        "html",
        "scss",
        "css",
        "\u0641\u0631\u0648\u0646\u062a",
        "\u0648\u0627\u062c\u0647\u0629",
        "\u0627\u0646\u062c\u0644\u0648\u0631"
    ];

    private static readonly string[] ArchitectureTerms =
    [
        "architecture",
        "roadmap",
        "design",
        "system",
        "agent",
        "\u062e\u0637\u0629",
        "\u0645\u0639\u0645\u0627\u0631\u064a\u0629"
    ];

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var transcript = string.Join("\n", request.Messages.Select(message => message.Content));
        var content = transcript.Contains("Return a JSON plan only", StringComparison.OrdinalIgnoreCase)
            ? CreatePlan(transcript)
            : transcript.Contains("Classify the user's message", StringComparison.OrdinalIgnoreCase)
                ? CreateUnderstanding(transcript)
                : transcript.Contains("Observations:", StringComparison.OrdinalIgnoreCase)
                    ? CreateFinalAnswer(transcript)
                    : CreateDirectReply(request);

        return Task.FromResult(new ChatResponse(content, "thoth-self"));
    }

    private static string CreatePlan(string transcript)
    {
        var goal = ExtractLineAfterLabel(transcript, "Goal:");
        var lower = goal.ToLowerInvariant();
        var steps = new List<Dictionary<string, object?>>
        {
            Step("Check internal memory for relevant prior context.", "memory.search", new Dictionary<string, string?>
            {
                ["query"] = goal,
                ["limit"] = "6"
            }),
            Step("Build a project-level summary before deeper inspection.", "workspace.summary", new Dictionary<string, string?>
            {
                ["maxEntries"] = "60"
            }),
            Step("Map the workspace to understand the current project shape.", "workspace.map", new Dictionary<string, string?>
            {
                ["maxDepth"] = "5"
            })
        };

        var path = ExtractLikelyPath(goal);
        if (!string.IsNullOrWhiteSpace(path))
        {
            steps.Add(Step("Read the file explicitly mentioned by the user.", "file.read", new Dictionary<string, string?>
            {
                ["path"] = path,
                ["maxChars"] = "30000"
            }));
        }

        if (LooksLikeBackendTask(lower))
        {
            steps.Add(Step("Read the API entry point.", "file.read", new Dictionary<string, string?>
            {
                ["path"] = "src/Thoth.Api/Program.cs",
                ["maxChars"] = "50000"
            }));
            steps.Add(Step("Inspect API contracts.", "file.read", new Dictionary<string, string?>
            {
                ["path"] = "src/Thoth.Api/Contracts/ApiContracts.cs",
                ["maxChars"] = "25000"
            }));
            steps.Add(Step("Find mapped HTTP routes.", "file.search", new Dictionary<string, string?>
            {
                ["query"] = "Map",
                ["maxResults"] = "80"
            }));
        }

        if (LooksLikeFrontendTask(lower))
        {
            steps.Add(Step("Read the Angular component logic.", "file.read", new Dictionary<string, string?>
            {
                ["path"] = "src/Thoth.Web/src/app/app.ts",
                ["maxChars"] = "50000"
            }));
            steps.Add(Step("Read the Angular template.", "file.read", new Dictionary<string, string?>
            {
                ["path"] = "src/Thoth.Web/src/app/app.html",
                ["maxChars"] = "50000"
            }));
            steps.Add(Step("Read the Angular stylesheet.", "file.read", new Dictionary<string, string?>
            {
                ["path"] = "src/Thoth.Web/src/app/app.scss",
                ["maxChars"] = "35000"
            }));
        }

        if (LooksLikeArchitectureTask(lower))
        {
            steps.Add(Step("Inspect architecture docs.", "file.read", new Dictionary<string, string?>
            {
                ["path"] = "docs/architecture.md",
                ["maxChars"] = "25000"
            }));
            steps.Add(Step("Inspect the roadmap.", "file.read", new Dictionary<string, string?>
            {
                ["path"] = "docs/roadmap.md",
                ["maxChars"] = "25000"
            }));
        }

        var query = ExtractSearchQuery(goal);
        if (!string.IsNullOrWhiteSpace(query))
        {
            steps.Add(Step("Search the workspace for goal-relevant text.", "file.search", new Dictionary<string, string?>
            {
                ["query"] = query,
                ["maxResults"] = "40"
            }));
        }

        var payload = new Dictionary<string, object?>
        {
            ["summary"] = "Self-contained reasoning plan generated by Thoth.",
            ["steps"] = steps
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string CreateUnderstanding(string transcript)
    {
        var message = ExtractAfterLabel(transcript, "User message:");
        var lower = message.ToLowerInvariant();
        var hasArabic = HasArabic(message);
        var requiresTools = LooksLikeBackendTask(lower) ||
                            LooksLikeFrontendTask(lower) ||
                            LooksLikeArchitectureTask(lower) ||
                            ContainsAny(lower, "code", "file", "project", "repo", "workspace", "build", "test", "bug", "implement", "refactor") ||
                            hasArabic && ContainsAny(message, ArabicWorkspaceTerms);

        var topic = LooksLikeFrontendTask(lower) ? "frontend" :
            LooksLikeBackendTask(lower) ? "backend" :
            LooksLikeArchitectureTask(lower) ? "architecture" :
            requiresTools ? "workspace" : "general";

        var payload = new
        {
            intent = requiresTools ? "workspace_task" : "general_chat",
            topic,
            language = hasArabic ? "ar" : "en",
            requiresTools,
            requiresVision = transcript.Contains("image/", StringComparison.OrdinalIgnoreCase),
            isLongContext = message.Length > 8000,
            confidence = requiresTools ? 0.84 : 0.64,
            summary = SingleLine(message, 220)
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string CreateFinalAnswer(string transcript)
    {
        var goal = ExtractBetweenLabels(transcript, "Goal:", "Plan:");
        var observations = ExtractBetweenLabels(transcript, "Observations:", "Write a direct");
        var lowerGoal = goal.ToLowerInvariant();
        var arabic = HasArabic(goal);
        var toolNames = Regex.Matches(observations, @"Tool:\s*(?<tool>[\w.]+)")
            .Select(match => match.Groups["tool"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine(arabic
            ? "\u0631\u0627\u062c\u0639\u062a \u0627\u0644\u0637\u0644\u0628 \u0628\u0623\u062f\u0648\u0627\u062a Thoth \u0627\u0644\u0645\u062d\u0644\u064a\u0629."
            : "I inspected the workspace with Thoth's local tools.");
        if (arabic)
        {
            builder.AppendLine("Arabic request detected and routed through internal understanding rules.");
        }

        builder.AppendLine();
        builder.AppendLine("Intent understood:");
        builder.AppendLine($"- {SingleLine(goal, 260)}");
        builder.AppendLine();

        if (LooksLikeBackendTask(lowerGoal))
        {
            AppendBackendFindings(builder, observations);
        }
        else if (LooksLikeFrontendTask(lowerGoal))
        {
            AppendFrontendFindings(builder, observations);
        }
        else
        {
            AppendGeneralFindings(builder, observations);
        }

        builder.AppendLine();
        builder.AppendLine("Tools used:");
        if (toolNames.Length == 0)
        {
            builder.AppendLine("- none");
        }
        else
        {
            foreach (var toolName in toolNames)
            {
                builder.AppendLine($"- {toolName}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Next best move:");
        builder.AppendLine(SuggestNextMove(lowerGoal));

        return builder.ToString().Trim();
    }

    private static string CreateDirectReply(ChatRequest request)
    {
        var lastUser = request.Messages.LastOrDefault(message => message.Role == ChatRole.User);
        var text = lastUser?.Content.Trim() ?? string.Empty;
        var arabic = HasArabic(text);
        var hasImage = lastUser?.Attachments?.Any(attachment => attachment.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) == true;
        var lower = text.ToLowerInvariant();

        if (IsGreeting(lower, text))
        {
            return arabic
                ? "\u0623\u0647\u0644\u0627 \u064a\u0627 \u0645\u0639\u0644\u0645. \u0623\u0646\u0627 \u062c\u0627\u0647\u0632. \u0627\u0643\u062a\u0628\u0644\u064a \u0627\u0644\u0644\u064a \u0639\u0627\u064a\u0632 \u062a\u0628\u0646\u064a\u0647 \u0623\u0648 \u062a\u0635\u0644\u062d\u0647\u060c \u0648\u0644\u0648 \u0627\u0644\u0637\u0644\u0628 \u0639\u0644\u0649 \u0627\u0644\u0645\u0634\u0631\u0648\u0639 \u0647\u0634\u063a\u0644 \u0627\u0644\u0623\u062f\u0648\u0627\u062a \u0648\u0623\u0641\u062a\u0634 \u0641\u064a \u0627\u0644\u0645\u0644\u0641\u0627\u062a."
                : "Hey, I am here. Tell me what you want to build, fix, inspect, or improve. If it touches the project, keep Tools enabled and I will inspect the workspace before answering.";
        }

        if (WantsCapabilities(lower, text))
        {
            return arabic
                ? "\u0623\u0642\u062f\u0631 \u0623\u0633\u0627\u0639\u062f\u0643 \u0641\u064a \u0627\u0644\u0643\u0648\u062f\u060c \u0627\u0644\u0648\u0627\u062c\u0647\u0629\u060c \u0627\u0644\u0640 backend\u060c \u0645\u0631\u0627\u062c\u0639\u0629 \u0627\u0644\u0645\u0634\u0631\u0648\u0639\u060c \u0648\u062a\u0644\u062e\u064a\u0635 \u0627\u0644\u0645\u0644\u0641\u0627\u062a. \u0644\u0645\u0627 \u0627\u0644\u0637\u0644\u0628 \u064a\u062d\u062a\u0627\u062c \u0633\u064a\u0627\u0642\u060c \u0647\u0633\u062a\u062e\u062f\u0645 \u0623\u062f\u0648\u0627\u062a Thoth \u0627\u0644\u062f\u0627\u062e\u0644\u064a\u0629 \u0628\u062f\u0644 \u0627\u0644\u062a\u062e\u0645\u064a\u0646."
                : "I can help with code, UI, backend APIs, project review, file analysis, memory, and workspace inspection. For anything project-related, I should use Thoth's tools first so the answer is based on the actual files instead of guessing.";
        }

        var builder = new StringBuilder();
        builder.AppendLine(arabic
            ? "\u0641\u0647\u0645\u062a \u0637\u0644\u0628\u0643."
            : "I understand.");

        if (!string.IsNullOrWhiteSpace(text))
        {
            builder.AppendLine(arabic ? "\u0627\u0644\u0645\u0642\u0635\u0648\u062f:" : "Request:");
            builder.AppendLine($"- {SingleLine(text, 320)}");
        }

        if (hasImage)
        {
            builder.AppendLine();
            builder.AppendLine(arabic
                ? "\u0627\u0644\u0635\u0648\u0631\u0629 \u0648\u0635\u0644\u062a \u0648\u0647\u062a\u062a\u062e\u0632\u0646 \u0645\u0639 \u0627\u0644\u0645\u062d\u0627\u062f\u062b\u0629. \u062a\u0641\u0633\u064a\u0631 \u0627\u0644\u0635\u0648\u0631 \u0646\u0641\u0633\u0647 \u0645\u062d\u062a\u0627\u062c \u0645\u0648\u062f\u064a\u0648\u0644 vision \u062f\u0627\u062e\u0644\u064a."
                : "The image/file is attached and stored with the conversation. Pixel-level image interpretation still needs an internal vision module.");
        }

        builder.AppendLine();
        builder.AppendLine(arabic
            ? "\u0627\u0644\u062e\u0637\u0648\u0629 \u0627\u0644\u062c\u0627\u064a\u0629: \u0627\u062f\u064a\u0646\u064a \u0647\u062f\u0641 \u0645\u062d\u062f\u062f \u0623\u0648 \u0627\u0641\u062a\u062d Tools \u0644\u0648 \u0639\u0627\u064a\u0632\u0646\u064a \u0623\u0641\u062a\u0634 \u062c\u0648\u0647 \u0627\u0644\u0645\u0634\u0631\u0648\u0639."
            : "Next step: give me a concrete goal, or keep Tools enabled if you want me to inspect the project before answering.");
        return builder.ToString().Trim();
    }

    private static void AppendBackendFindings(StringBuilder builder, string observations)
    {
        var routes = Regex.Matches(observations, @"app\.Map(?<verb>Get|Post|Patch|Delete|Put)\(\""(?<path>[^\""]+)\""")
            .Select(match => $"{match.Groups["verb"].Value.ToUpperInvariant()} {match.Groups["path"].Value}")
            .Concat(Regex.Matches(observations, @"-\s*(?<verb>GET|POST|PATCH|DELETE|PUT)\s+(?<path>/[^\s(]+)")
                .Select(match => $"{match.Groups["verb"].Value} {match.Groups["path"].Value}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        builder.AppendLine("Backend findings:");
        if (routes.Length == 0)
        {
            builder.AppendLine("- I inspected the backend surface but did not find route declarations in the returned snippets.");
        }
        else
        {
            foreach (var route in routes.Take(30))
            {
                builder.AppendLine($"- {route}");
            }
        }

        if (observations.Contains("ChatOrchestrator", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("- Chat routing is centralized through the conversation orchestrator.");
        }

        if (observations.Contains("AttachmentStorageService", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("- Attachment storage is handled by a dedicated service.");
        }
    }

    private static void AppendFrontendFindings(StringBuilder builder, string observations)
    {
        builder.AppendLine("Frontend findings:");
        AppendIfPresent(builder, observations, "sidebar", "The UI has a conversation sidebar.");
        AppendIfPresent(builder, observations, "composer", "The UI has a message composer with attachment handling.");
        AppendIfPresent(builder, observations, "inspector", "The UI has an inspector panel for run/tool/memory details.");
        AppendIfPresent(builder, observations, "pendingFiles", "The UI tracks pending uploads before send.");
        AppendIfPresent(builder, observations, "MarkdownPipe", "Assistant messages are rendered as Markdown.");
        AppendIfPresent(builder, observations, "workspaceSummary", "The UI includes a workspace summary panel.");
    }

    private static void AppendGeneralFindings(StringBuilder builder, string observations)
    {
        builder.AppendLine("Key findings:");
        var lines = observations
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line =>
                !line.StartsWith("Step ", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("Tool:", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("Succeeded:", StringComparison.OrdinalIgnoreCase))
            .Where(line => line.Length > 2)
            .Take(12)
            .ToArray();

        if (lines.Length == 0)
        {
            builder.AppendLine("- No detailed observations were produced.");
            return;
        }

        foreach (var line in lines)
        {
            builder.AppendLine($"- {SingleLine(line, 180)}");
        }
    }

    private static string SuggestNextMove(string lowerGoal)
    {
        if (LooksLikeBackendTask(lowerGoal))
        {
            return "- Add structured endpoint tests and route metadata so the UI can display API capability coverage.";
        }

        if (LooksLikeFrontendTask(lowerGoal))
        {
            return "- Wire workspace/system panels to backend summary endpoints and expand loading/error states.";
        }

        return "- Ask for a concrete inspect/build/refactor goal and I will route it through the internal tools.";
    }

    private static void AppendIfPresent(StringBuilder builder, string source, string needle, string line)
    {
        if (source.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine($"- {line}");
        }
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

    private static string ExtractBetweenLabels(string text, string startLabel, string endLabel)
    {
        var startIndex = text.IndexOf(startLabel, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
        {
            return string.Empty;
        }

        startIndex += startLabel.Length;
        var endIndex = text.IndexOf(endLabel, startIndex, StringComparison.OrdinalIgnoreCase);
        return endIndex < 0 ? text[startIndex..].Trim() : text[startIndex..endIndex].Trim();
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
        var match = Regex.Match(goal, @"(?<path>[\w./\\-]+\.(cs|ts|html|scss|json|md|txt|sln|csproj|yml|yaml|xml))", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["path"].Value : string.Empty;
    }

    private static string ExtractSearchQuery(string goal)
    {
        var words = Regex.Matches(goal, @"[\p{L}\p{N}_-]{3,}")
            .Select(match => match.Value)
            .Where(word => !StopWords.Contains(word.ToLowerInvariant()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();

        return string.Join(' ', words);
    }

    private static bool LooksLikeBackendTask(string lower) =>
        ContainsAny(lower, BackendTerms);

    private static bool LooksLikeFrontendTask(string lower) =>
        ContainsAny(lower, FrontendTerms);

    private static bool LooksLikeArchitectureTask(string lower) =>
        ContainsAny(lower, ArchitectureTerms);

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static bool HasArabic(string text) => text.Any(c => c >= 0x0600 && c <= 0x06FF);

    private static bool IsGreeting(string lower, string text)
    {
        var normalized = lower.Trim().Trim('.', '!', '?');
        return normalized is "hi" or "hello" or "hey" or "yo" or "sup" ||
               ContainsAny(text, "\u0627\u0647\u0644\u0627", "\u0623\u0647\u0644\u0627", "\u0645\u0631\u062d\u0628\u0627", "\u0633\u0644\u0627\u0645", "\u0627\u0632\u064a\u0643", "\u0639\u0627\u0645\u0644 \u0627\u064a\u0647");
    }

    private static bool WantsCapabilities(string lower, string text) =>
        ContainsAny(lower, "what can you do", "help me", "capabilities", "who are you") ||
        ContainsAny(text, "\u062a\u0642\u062f\u0631 \u062a\u0639\u0645\u0644 \u0627\u064a\u0647", "\u0627\u0646\u062a \u0645\u064a\u0646", "\u0633\u0627\u0639\u062f\u0646\u064a");

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
        "project",
        "current"
    ];
}
