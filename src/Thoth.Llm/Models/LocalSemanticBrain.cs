using System.Text;
using System.Text.RegularExpressions;

namespace Thoth.Llm.Models;

internal static class LocalSemanticBrain
{
    private const int Dimensions = 192;

    private static readonly TrainedPattern[] TopicPatterns = Train(
    [
        new("backend", "backend api endpoint route http asp.net controller server program.cs swagger request response middleware chat orchestrator"),
        new("backend", "c# csharp .net dotnet method function class service controller repository async task linq"),
        new("backend", "\u0628\u0627\u0643 api endpoint http request server swagger"),
        new("backend", "\u0643\u0648\u062f \u0645\u064a\u062b\u0648\u062f \u0645\u064a\u062b\u062f \u062f\u0627\u0644\u0629 \u0643\u0644\u0627\u0633 \u0633\u064a \u0634\u0627\u0631\u0628"),
        new("coding", "write generate code snippet method function class c# csharp .net dotnet"),
        new("coding", "\u0627\u0643\u062a\u0628 \u0643\u0648\u062f \u0627\u0639\u0645\u0644 \u0645\u064a\u062b\u0648\u062f \u0645\u064a\u062b\u062f \u062f\u0627\u0644\u0629 \u0643\u0644\u0627\u0633"),
        new("frontend", "frontend angular ui component html scss css browser user experience chat composer sidebar panel"),
        new("frontend", "\u0641\u0631\u0648\u0646\u062a \u0648\u0627\u062c\u0647\u0629 \u0627\u0646\u062c\u0644\u0648\u0631 ui"),
        new("model", "model llm reasoning brain neural think intelligence train understand planner embeddings semantic agent cognition"),
        new("model", "\u0645\u0648\u062f\u064a\u0644 \u064a\u0641\u0643\u0631 \u0639\u0642\u0644 \u0630\u0643\u064a \u062a\u062f\u0631\u064a\u0628 \u0639\u0635\u0628\u064a \u064a\u0641\u0647\u0645"),
        new("research", "web internet online google search latest current news price weather external source cite summarize"),
        new("research", "\u0627\u0628\u062d\u062b \u0628\u062d\u062b \u062f\u0648\u0631 \u0627\u0644\u0646\u062a \u062c\u0648\u062c\u0644 \u0648\u064a\u0628 \u0627\u062e\u0628\u0627\u0631 \u0627\u062d\u062f\u062b \u0644\u062e\u0635 \u0645\u0635\u0627\u062f\u0631"),
        new("architecture", "architecture roadmap design system pipeline module runtime memory sandbox tools architecture"),
        new("debugging", "bug broken error failing not working weak crash exception returns nothing issue problem fix"),
        new("debugging", "\u0645\u0634\u0643\u0644\u0629 \u0628\u0627\u064a\u0638 \u0639\u0637\u0644\u0627\u0646 \u0636\u0639\u064a\u0641 \u0645\u0628\u064a\u0631\u062f\u0634 \u0645\u0628\u064a\u0641\u0643\u0631\u0634"),
        new("testing", "test build verify validation smoke coverage failing passing regression"),
        new("workspace", "project workspace repo files code inspect search read summarize")
    ]);

    private static readonly TrainedPattern[] ActionPatterns = Train(
    [
        new("inspect", "inspect analyze review summarize explain list show understand map"),
        new("build", "build create implement add wire scaffold develop make"),
        new("fix", "fix repair improve enhance debug solve broken issue weak"),
        new("train", "train learn neural model intelligence brain embeddings patterns corpus"),
        new("test", "test verify validate build smoke check coverage"),
        new("search", "search research look up browse read sources summarize cite"),
        new("search", "\u0627\u0628\u062d\u062b \u0628\u062d\u062b \u062f\u0648\u0631 \u0627\u0644\u0646\u062a \u0644\u062e\u0635 \u0645\u0635\u0627\u062f\u0631"),
        new("chat", "hello hi question answer talk")
    ]);

    private static readonly string[] ResearchTerms =
    [
        "web",
        "internet",
        "online",
        "google",
        "search the web",
        "web search",
        "look up",
        "lookup",
        "research",
        "latest",
        "current",
        "today",
        "news",
        "price",
        "weather",
        "\u0627\u0628\u062d\u062b",
        "\u0628\u062d\u062b",
        "\u062f\u0648\u0631",
        "\u0627\u0644\u0646\u062a",
        "\u062c\u0648\u062c\u0644",
        "\u0648\u064a\u0628",
        "\u0623\u062e\u0628\u0627\u0631",
        "\u0627\u062e\u0628\u0627\u0631",
        "\u0622\u062e\u0631",
        "\u0627\u062d\u062f\u062b",
        "\u0623\u062d\u062f\u062b",
        "\u062d\u0627\u0644\u064a",
        "\u0633\u0639\u0631",
        "\u0644\u062e\u0635"
    ];

    private static readonly string[] LocalSearchScopeTerms =
    [
        "workspace",
        "repo",
        "repository",
        "codebase",
        "project",
        "file",
        "src/",
        "tests/",
        ".cs",
        ".ts",
        ".html",
        ".scss",
        ".json",
        ".md",
        "\u0627\u0644\u0645\u0634\u0631\u0648\u0639",
        "\u0645\u0634\u0631\u0648\u0639",
        "\u0627\u0644\u0645\u0644\u0641",
        "\u0645\u0644\u0641",
        "\u0627\u0644\u0631\u064a\u0628\u0648",
        "\u0631\u064a\u0628\u0648",
        "\u0627\u0644\u0643\u0648\u062f",
        "\u0643\u0648\u062f"
    ];

    public static BrainSignal AnalyzeGoal(string goal)
    {
        var text = goal.Trim();
        var lower = text.ToLowerInvariant();
        var language = HasArabic(text) ? "ar" : "en";
        var keywords = ExtractKeywords(text, 8);
        var codeGeneration = LooksLikeGenericCodeGeneration(lower, text);
        var researchTask = LooksLikeResearchTask(lower, text);
        var projectBound = !researchTask && LooksLikeProjectBoundTask(lower, text);

        if (IsCasualChat(lower, text))
        {
            return new BrainSignal("general", "chat", language, 0.92, SingleLine(text, 240), keywords, false);
        }

        var topic = Classify(text, TopicPatterns, "general", out var topicScore);
        var action = Classify(text, ActionPatterns, "chat", out var actionScore);

        topic = OverrideTopic(lower, topic);
        action = OverrideAction(lower, action);

        if (researchTask)
        {
            topic = "research";
            if (action == "chat")
            {
                action = "search";
            }
        }

        if (codeGeneration && !projectBound && !researchTask)
        {
            topic = "coding";
            if (action == "chat")
            {
                action = "build";
            }
        }

        var explicitToolSignal = HasExplicitToolSignal(lower, text);
        if (!explicitToolSignal && !codeGeneration && keywords.Count <= 3)
        {
            topic = "general";
            action = "chat";
        }

        var requiresTools = explicitToolSignal &&
                            (topic is not "general" ||
                              action is "inspect" or "build" or "fix" or "train" or "test" or "search");
        if (researchTask)
        {
            requiresTools = true;
        }
        if (codeGeneration && !projectBound && !researchTask)
        {
            requiresTools = false;
        }

        var confidence = Math.Clamp(Math.Max(topicScore, actionScore), 0.58, 0.95);
        return new BrainSignal(topic, action, language, confidence, SingleLine(text, 240), keywords, requiresTools);
    }

    public static ObservationInsights AnalyzeObservations(string goal, string observations)
    {
        var routes = ExtractRoutes(observations);
        var tools = Regex.Matches(observations, @"Tool:\s*(?<tool>[\w.]+)")
            .Select(match => match.Groups["tool"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();
        var files = Regex.Matches(observations, @"(?<file>(?:src|tests|docs|configs)[\\/][\w .\\/.-]+\.(?:cs|ts|html|scss|json|md|csproj))")
            .Select(match => match.Groups["file"].Value.Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToArray();
        var symbols = Regex.Matches(observations, @"\b(?:class|interface|record)\s+(?<name>[A-Z][A-Za-z0-9_]+)|\b(?<name>[A-Z][A-Za-z0-9_]+(?:Service|Model|Engine|Planner|Tool|Store|Controller|Options))\b")
            .Select(match => match.Groups["name"].Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(18)
            .ToArray();
        var failures = ExtractLines(observations)
            .Where(IsRuntimeProblem)
            .Select(line => SingleLine(line, 180))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        var rankedEvidence = RankEvidence(goal, observations)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();
        var researchTask = LooksLikeResearchTask(goal.ToLowerInvariant(), goal);
        var evidence = researchTask
            ? ExtractResearchEvidence(observations)
                .Concat(rankedEvidence.Where(IsResearchEvidenceLine))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToArray()
            : rankedEvidence;

        return new ObservationInsights(routes, tools, files, symbols, failures, evidence);
    }

    public static string BuildFinalAnswer(string goal, string observations)
    {
        var signal = AnalyzeGoal(goal);
        var insights = AnalyzeObservations(goal, observations);
        var answer = ComposeEvidenceAnswer(signal, insights);
        return RefineEvidenceAnswer(goal, answer, signal, insights).Trim();
    }

    public static string BuildDirectReply(string text, bool hasImage)
    {
        var frame = BuildCognitiveFrame(text, hasImage);
        var answer = DraftDirectAnswer(frame);

        for (var cycle = 1; cycle <= 3; cycle++)
        {
            var critique = CritiqueDraft(frame, answer);
            answer = ImproveDraft(frame, answer, critique, cycle);
            if (critique.Issues.Count == 0)
            {
                break;
            }
        }

        return answer.Trim();
    }

    private static string ComposeEvidenceAnswer(BrainSignal signal, ObservationInsights insights)
    {
        var rows = new List<string>
        {
            Field(signal, "request", signal.Summary),
            Field(signal, "intent", $"{signal.Topic}/{signal.Action} {signal.Confidence:0.00}")
        };

        foreach (var item in BuildFindingItems(signal, insights).Take(10))
        {
            rows.Add(Field(signal, "evidence", item));
        }

        if (insights.ToolNames.Count > 0)
        {
            rows.Add(Field(signal, "tools", string.Join(", ", insights.ToolNames.Take(8))));
        }

        rows.Add(Field(signal, "next", BuildNextActionToken(signal, insights)));
        return string.Join(Environment.NewLine, rows);
    }

    private static IEnumerable<string> BuildFindingItems(BrainSignal signal, ObservationInsights insights)
    {
        if (signal.Topic == "backend" && insights.Routes.Count > 0)
        {
            yield return $"routes:{insights.Routes.Count}";
            foreach (var group in GroupRoutes(insights.Routes).Take(8))
            {
                yield return $"{group.Key}:{string.Join(",", group.Value.Take(8))}";
            }
        }

        if (signal.Topic == "model")
        {
            foreach (var symbol in insights.Symbols.Where(symbol => ContainsAny(symbol, "Model", "Engine", "Planner", "Understanding", "Orchestrator")).Take(8))
            {
                yield return $"symbol:{symbol}";
            }
        }

        if (signal.Topic == "frontend")
        {
            foreach (var file in insights.Files.Where(file => file.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".scss", StringComparison.OrdinalIgnoreCase)).Take(8))
            {
                yield return $"ui-file:{file}";
            }
        }

        foreach (var failure in insights.Failures.Take(5))
        {
            yield return $"problem:{failure}";
        }

        foreach (var evidence in insights.Evidence.Where(line => !line.StartsWith("Workspace:", StringComparison.OrdinalIgnoreCase)).Take(8))
        {
            yield return SingleLine(evidence, 220);
        }

        if (insights.Routes.Count == 0 && insights.Evidence.Count == 0 && insights.Failures.Count == 0 && insights.Files.Count == 0)
        {
            yield return $"evidence-gap:{signal.Topic}/{signal.Action}";
        }
    }

    private static string BuildNextActionToken(BrainSignal signal, ObservationInsights insights)
    {
        var basis = signal.Topic switch
        {
            "backend" => insights.Routes.Count > 0 ? "endpoint-contract-test" : "route-read",
            "frontend" => insights.Files.Count > 0 ? "component-state-browser-check" : "ui-file-read",
            "model" => "pattern-bank-regression",
            "research" => insights.Evidence.Count > 1 ? "source-compare-cite" : "source-expand",
            "debugging" => insights.Failures.Count > 0 ? "reproduce-first-failure" : "trace-failure",
            "testing" => "smallest-test-then-coverage",
            _ => $"{signal.Topic}-{signal.Action}"
        };

        return $"{basis}:{string.Join(",", signal.Keywords.Take(4))}";
    }

    private static CognitiveFrame BuildCognitiveFrame(string text, bool hasImage)
    {
        var cleaned = string.IsNullOrWhiteSpace(text) ? "(empty message)" : text.Trim();
        var signal = AnalyzeGoal(cleaned);
        var clauses = SplitClauses(cleaned).Take(8).ToArray();
        var constraints = ExtractConstraints(cleaned).Take(8).ToArray();
        var questionKind = InferQuestionKind(cleaned.ToLowerInvariant(), cleaned, signal);

        return new CognitiveFrame(
            cleaned,
            signal,
            clauses,
            signal.Keywords,
            constraints,
            questionKind,
            hasImage);
    }

    private static string DraftDirectAnswer(CognitiveFrame frame)
    {
        var rows = new List<string>
        {
            Field(frame.Signal, "request", SingleLine(frame.Text, 180)),
            Field(frame.Signal, "intent", $"{frame.Signal.Topic}/{frame.Signal.Action} {frame.Signal.Confidence:0.00}")
        };

        if (frame.Clauses.Count > 1)
        {
            rows.Add(Field(frame.Signal, "parts", string.Join(" | ", frame.Clauses.Select(clause => SingleLine(clause, 90)))));
        }

        if (frame.FocusTerms.Count > 0)
        {
            rows.Add(Field(frame.Signal, "terms", string.Join(", ", frame.FocusTerms.Take(7))));
        }

        if (frame.Constraints.Count > 0)
        {
            rows.Add(Field(frame.Signal, "constraints", string.Join(" | ", frame.Constraints.Select(item => SingleLine(item, 90)))));
        }

        rows.Add(Field(frame.Signal, "route", string.Join(" -> ", BuildDirectRoute(frame))));

        if (frame.HasImage)
        {
            rows.Add(Field(frame.Signal, "attachment", frame.Signal.Language == "ar"
                ? "\u0635\u0648\u0631\u0629:\u0631\u0624\u064a\u0629-\u0645\u062d\u0644\u064a\u0629"
                : "image:local-vision"));
        }

        return string.Join(Environment.NewLine, rows);
    }

    private static IReadOnlyList<string> BuildDirectRoute(CognitiveFrame frame)
    {
        var route = new List<string>
        {
            $"parse:{string.Join("+", frame.Clauses.Take(3).Select(clause => string.Join("-", ExtractKeywords(clause, 3))))}",
            $"classify:{frame.Signal.Topic}/{frame.Signal.Action}"
        };

        if (frame.QuestionKind == "code")
        {
            route.Add($"contract:{string.Join(",", CodeContractSlots(frame))}");
        }

        if (frame.QuestionKind == "research")
        {
            route.Add($"web:{string.Join(",", frame.FocusTerms.Take(4))}");
            route.Add("sources:read");
        }

        if (frame.Signal.RequiresTools)
        {
            route.Add($"tools:{ToolRouteName(frame.Signal)}");
        }

        if (frame.QuestionKind == "capability")
        {
            route.Add($"capabilities:{string.Join(",", InferCapabilityTokens(frame))}");
        }

        if (frame.QuestionKind is "general" or "conversation" or "understanding")
        {
            route.Add($"answer:{SingleLine(frame.Signal.Summary, 80)}");
        }

        route.Add("revise:semantic-fit");
        return route;
    }

    private static IReadOnlyList<string> CodeContractSlots(CognitiveFrame frame)
    {
        var slots = new List<string>();
        if (!frame.FocusTerms.Any(term => term.Contains("name", StringComparison.OrdinalIgnoreCase) || term.Contains("\u0627\u0633\u0645", StringComparison.OrdinalIgnoreCase)))
        {
            slots.Add("name");
        }

        slots.Add("inputs");
        slots.Add("output");
        slots.Add("validation");
        return slots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> InferCapabilityTokens(CognitiveFrame frame)
    {
        var tokens = new List<string> { "parse", "rank", "act", "review" };
        if (frame.FocusTerms.Any(term => ContainsAny(term, "file", "project", "repo", "workspace")))
        {
            tokens.Add("workspace");
        }

        tokens.Add("web");
        tokens.Add("memory");
        return tokens.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ToolRouteName(BrainSignal signal) =>
        signal.Topic switch
        {
            "research" => "web.research",
            "frontend" or "backend" or "model" or "workspace" or "debugging" => "workspace+file",
            _ => signal.RequiresTools ? "agent-tools" : "none"
        };

    private static string Field(BrainSignal signal, string key, string value)
    {
        var label = signal.Language == "ar"
            ? key switch
            {
                "request" => "\u0637\u0644\u0628",
                "intent" => "\u0646\u064a\u0629",
                "parts" => "\u0623\u062c\u0632\u0627\u0621",
                "terms" => "\u0643\u0644\u0645\u0627\u062a",
                "constraints" => "\u0642\u064a\u0648\u062f",
                "route" => "\u0645\u0633\u0627\u0631",
                "attachment" => "\u0645\u0631\u0641\u0642",
                "evidence" => "\u062f\u0644\u064a\u0644",
                "tools" => "\u0623\u062f\u0648\u0627\u062a",
                "next" => "\u062a\u0627\u0644\u064a",
                "revision" => "\u062a\u062d\u0633\u064a\u0646",
                _ => key
            }
            : key;

        return $"{label}: {value}";
    }

    private static CognitiveCritique CritiqueDraft(CognitiveFrame frame, string answer)
    {
        var issues = new List<string>();

        if (!answer.Contains(frame.Signal.Topic, StringComparison.OrdinalIgnoreCase) &&
            !answer.Contains(frame.Signal.Action, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("classification_not_visible");
        }

        if (frame.FocusTerms.Count > 0 &&
            !frame.FocusTerms.Take(3).Any(term => answer.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add("focus_terms_missing");
        }

        if (frame.HasImage &&
            !ContainsAny(answer, "image", "attachment", "\u0635\u0648\u0631\u0629", "\u0645\u0631\u0641\u0642"))
        {
            issues.Add("image_not_acknowledged");
        }

        if (frame.Signal.RequiresTools &&
            !ContainsAny(answer, "tool", "web.research", "files", "memory", "\u0623\u062f\u0648\u0627\u062a", "\u0645\u0644\u0641\u0627\u062a", "\u0648\u064a\u0628", "\u0630\u0627\u0643\u0631\u0629"))
        {
            issues.Add("tool_path_missing");
        }

        if (frame.QuestionKind == "code" &&
            !ContainsAny(answer, "inputs", "output", "validation", "contract", "\u0645\u062f\u062e\u0644\u0627\u062a", "\u062e\u0631\u062c", "\u0627\u0644\u0645\u064a\u062b\u0648\u062f"))
        {
            issues.Add("code_contract_missing");
        }

        if (frame.QuestionKind == "research" &&
            !ContainsAny(answer, "source", "sources", "web", "research", "\u0645\u0635\u0627\u062f\u0631", "\u0648\u064a\u0628", "\u0628\u062d\u062b"))
        {
            issues.Add("research_path_missing");
        }

        var score = Math.Clamp(1 - issues.Count * 0.18, 0.2, 0.98);
        return new CognitiveCritique(issues, score);
    }

    private static string ImproveDraft(CognitiveFrame frame, string answer, CognitiveCritique critique, int cycle)
    {
        if (critique.Issues.Count == 0)
        {
            return answer.Trim();
        }

        var revisions = critique.Issues
            .Select(issue => DescribeIssue(issue, frame))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return $"{answer.Trim()}{Environment.NewLine}{Field(frame.Signal, "revision", $"{cycle}:{string.Join(",", revisions)}")}";
    }

    private static string DescribeIssue(string issue, CognitiveFrame frame) =>
        issue switch
        {
            "classification_not_visible" => $"{frame.Signal.Topic}/{frame.Signal.Action}",
            "focus_terms_missing" => $"terms:{string.Join("+", frame.FocusTerms.Take(5))}",
            "image_not_acknowledged" => "image",
            "tool_path_missing" => $"tools:{ToolRouteName(frame.Signal)}",
            "code_contract_missing" => $"contract:{string.Join("+", CodeContractSlots(frame))}",
            "research_path_missing" => $"sources:{string.Join("+", frame.FocusTerms.Take(4))}",
            _ => issue
        };

    private static string RefineEvidenceAnswer(string goal, string answer, BrainSignal signal, ObservationInsights insights)
    {
        var refined = answer.Trim();
        var revisions = new List<string>();

        if (insights.ToolNames.Count > 0 &&
            !insights.ToolNames.Any(tool => refined.Contains(tool, StringComparison.OrdinalIgnoreCase)))
        {
            revisions.Add($"tools:{string.Join("+", insights.ToolNames.Take(5))}");
        }

        if (signal.Topic == "research" &&
            !ContainsAny(refined, "URL:", "http://", "https://"))
        {
            revisions.Add($"sources:{insights.Evidence.Count}");
        }

        if (!refined.Contains("next:", StringComparison.OrdinalIgnoreCase) &&
            !refined.Contains("\u062a\u0627\u0644\u064a:", StringComparison.OrdinalIgnoreCase))
        {
            revisions.Add(BuildNextActionToken(signal, insights));
        }

        return revisions.Count == 0
            ? refined
            : $"{refined}{Environment.NewLine}{Field(signal, "revision", string.Join(",", revisions.Distinct(StringComparer.OrdinalIgnoreCase)))}";
    }

    private static IReadOnlyList<string> SplitClauses(string text) =>
        Regex.Split(text, "[\\r\\n]+|(?<=[\\.\\?!;:\\u060C\\u061B\\u061F])\\s+|[,;\\u060C\\u061B]+")
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> ExtractConstraints(string text)
    {
        var lower = text.ToLowerInvariant();
        return SplitClauses(text)
            .Where(clause =>
            {
                var item = clause.ToLowerInvariant();
                return ContainsAny(item, "without", "never", "must", "local", "external", "source", "cite", "web", "recursive", "perfect") ||
                       ContainsAny(clause, "\u0645\u0646 \u063a\u064a\u0631", "\u0644\u0627\u0632\u0645", "\u0645\u062d\u0644\u064a", "\u062e\u0627\u0631\u062c\u064a", "\u0645\u0635\u0627\u062f\u0631", "\u0648\u064a\u0628", "\u0627\u0644\u0646\u062a", "\u064a\u0643\u0633\u0631", "\u064a\u0641\u0647\u0645");
            })
            .DefaultIfEmpty(ContainsAny(lower, "local", "recursive") ? SingleLine(text, 140) : string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string InferQuestionKind(string lower, string text, BrainSignal signal)
    {
        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["conversation"] = signal.Action == "chat" ? 2 : 0,
            ["capability"] = ContainsAny(lower, "what can you do", "capabilities", "who are you") ||
                              ContainsAny(text, "\u062a\u0642\u062f\u0631 \u062a\u0639\u0645\u0644 \u0627\u064a\u0647", "\u0627\u0646\u062a \u0645\u064a\u0646", "\u0633\u0627\u0639\u062f\u0646\u064a") ? 4 : 0,
            ["code"] = signal.Topic == "coding" || LooksLikeGenericCodeGeneration(lower, text) ? 4 : 0,
            ["research"] = signal.Topic == "research" ? 5 : 0,
            ["tool"] = signal.RequiresTools ? 4 : 0,
            ["understanding"] = IsUnderstandingCheck(lower, text) ? 4 : 0
        };

        var best = scores
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(pair => pair.Value > 0);

        return best.Value > 0 ? best.Key : "general";
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> GroupRoutes(IReadOnlyList<string> routes)
    {
        return routes
            .GroupBy(route =>
            {
                var path = route.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault() ?? "/";
                if (path == "/" || path.Equals("/health", StringComparison.OrdinalIgnoreCase))
                {
                    return "system";
                }

                var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length >= 2 && parts[0].Equals("api", StringComparison.OrdinalIgnoreCase)
                    ? parts[1]
                    : parts.FirstOrDefault() ?? "system";
            }, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(route => route).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static void AppendIfAny(StringBuilder builder, string title, IEnumerable<string> values)
    {
        var items = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        if (items.Length == 0)
        {
            return;
        }

        builder.AppendLine($"- {title}: {string.Join("; ", items)}");
    }

    private static string[] ExtractRoutes(string observations)
    {
        var mapRoutes = Regex.Matches(observations, @"app\.Map(?<verb>Get|Post|Put|Patch|Delete)\(\""(?<route>[^\""]+)\""")
            .Select(match => $"{match.Groups["verb"].Value.ToUpperInvariant()} {match.Groups["route"].Value}");
        var listedRoutes = Regex.Matches(observations, @"(?<![A-Z])(?<verb>GET|POST|PUT|PATCH|DELETE)\s+(?<route>/[^\s,)]+)")
            .Select(match => $"{match.Groups["verb"].Value} {match.Groups["route"].Value}");

        return mapRoutes
            .Concat(listedRoutes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray();
    }

    private static IEnumerable<string> RankEvidence(string goal, string observations)
    {
        var goalVector = Encode(goal);
        return ExtractLines(observations)
            .Where(line => IsUsefulEvidence(line))
            .Select(line => new
            {
                Line = SingleLine(line, 220),
                Score = Cosine(goalVector, Encode(line)) + KeywordOverlap(goal, line)
            })
            .Where(item => item.Score > 0.05)
            .OrderByDescending(item => item.Score)
            .Select(item => item.Line);
    }

    private static IEnumerable<string> ExtractResearchEvidence(string observations)
    {
        var capture = false;
        foreach (var rawLine in ExtractLines(observations))
        {
            var line = SingleLine(rawLine, 220);
            if (line.Contains("Tool: web.research", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Research query:", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Search results:", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Read summaries:", StringComparison.OrdinalIgnoreCase))
            {
                capture = true;
            }
            else if (line.Contains("Tool: memory.", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("Tool: workspace.", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("Tool: file.", StringComparison.OrdinalIgnoreCase))
            {
                capture = false;
            }

            if (capture && IsResearchEvidenceLine(line))
            {
                yield return line;
            }
        }
    }

    private static bool IsResearchEvidenceLine(string line) =>
        line.Contains("Research query:", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Search query:", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Source:", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("URL:", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Snippet:", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Source ", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("- ", StringComparison.OrdinalIgnoreCase) && !line.Contains("memory.", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ExtractLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsUsefulEvidence(string line)
    {
        if (line.Length < 4)
        {
            return false;
        }

        return !line.StartsWith("Step ", StringComparison.OrdinalIgnoreCase) &&
               !line.StartsWith("Tool:", StringComparison.OrdinalIgnoreCase) &&
               !line.StartsWith("Succeeded:", StringComparison.OrdinalIgnoreCase) &&
               !line.StartsWith("Files:", StringComparison.OrdinalIgnoreCase) &&
               !line.StartsWith("Directories:", StringComparison.OrdinalIgnoreCase) &&
               !line.StartsWith("[", StringComparison.Ordinal) &&
               !line.Contains("new(\"", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRuntimeProblem(string line)
    {
        if (line.Contains("new(\"", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("No matches found.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return line.Contains("Succeeded: False", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("=> failed", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("error:", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("BadRequest", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("timeout", StringComparison.OrdinalIgnoreCase);
    }

    private static string Classify(string text, IReadOnlyList<TrainedPattern> patterns, string fallback, out double score)
    {
        var vector = Encode(text);
        var best = patterns
            .Select(pattern => new { pattern.Label, Score = Cosine(vector, pattern.Vector) })
            .OrderByDescending(item => item.Score)
            .FirstOrDefault();

        score = best?.Score ?? 0;
        return best is null || best.Score < 0.14 ? fallback : best.Label;
    }

    private static TrainedPattern[] Train(IEnumerable<TrainingExample> examples)
    {
        return examples
            .GroupBy(example => example.Label, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var vectors = group.Select(example => Encode(example.Text)).ToArray();
                var centroid = new double[Dimensions];
                foreach (var vector in vectors)
                {
                    for (var index = 0; index < Dimensions; index++)
                    {
                        centroid[index] += vector[index] / vectors.Length;
                    }
                }

                NormalizeInPlace(centroid);
                return new TrainedPattern(group.Key, centroid);
            })
            .ToArray();
    }

    private static double[] Encode(string text)
    {
        var vector = new double[Dimensions];
        foreach (var token in Tokenize(text))
        {
            AddFeature(vector, token, 1.0);
            if (token.Length > 4)
            {
                AddFeature(vector, token[..4], 0.45);
                AddFeature(vector, token[^4..], 0.35);
            }
        }

        NormalizeInPlace(vector);
        return vector;
    }

    private static void AddFeature(double[] vector, string feature, double weight)
    {
        var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(feature);
        var index = Math.Abs(hash % Dimensions);
        var sign = (hash & 1) == 0 ? 1.0 : -1.0;
        vector[index] += sign * weight;
    }

    private static double Cosine(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var dot = 0.0;
        for (var index = 0; index < Dimensions; index++)
        {
            dot += left[index] * right[index];
        }

        return dot;
    }

    private static double KeywordOverlap(string left, string right)
    {
        var leftTokens = ExtractKeywords(left, 16).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (leftTokens.Count == 0)
        {
            return 0;
        }

        var rightTokens = ExtractKeywords(right, 32);
        return rightTokens.Count(token => leftTokens.Contains(token)) / (double)leftTokens.Count * 0.35;
    }

    private static void NormalizeInPlace(double[] vector)
    {
        var length = Math.Sqrt(vector.Sum(value => value * value));
        if (length <= 0)
        {
            return;
        }

        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] /= length;
        }
    }

    private static IReadOnlyList<string> ExtractKeywords(string text, int limit)
    {
        return Tokenize(text)
            .Where(token => !StopWords.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        foreach (Match match in Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}_-]{2,}"))
        {
            yield return match.Value;
        }
    }

    private static string OverrideTopic(string lower, string topic)
    {
        if (LooksLikeResearchTask(lower, lower))
        {
            return "research";
        }

        if (ContainsAny(lower, "model", "llm", "reason", "reasoning", "neural", "train", "brain", "think", "intelligence") ||
            ContainsAny(lower, "\u0645\u0648\u062f\u064a\u0644", "\u064a\u0641\u0643\u0631", "\u0630\u0643\u064a", "\u0639\u0642\u0644", "\u062a\u062f\u0631\u064a\u0628", "\u0639\u0635\u0628\u064a"))
        {
            return "model";
        }

        if (ContainsAny(lower, "backend", "endpoint", "api", "swagger", "controller", "route") ||
            ContainsAny(lower, "service") && LooksLikeProjectBoundTask(lower, lower) ||
            ContainsAny(lower, "\u0628\u0627\u0643") ||
            ContainsAny(lower, "\u0633\u064a \u0634\u0627\u0631\u0628") && LooksLikeProjectBoundTask(lower, lower))
        {
            return "backend";
        }

        if (ContainsAny(lower, "frontend", "angular", "component", "html", "scss") ||
            ContainsWholeWord(lower, "ui") ||
            ContainsAny(lower, "\u0641\u0631\u0648\u0646\u062a", "\u0648\u0627\u062c\u0647\u0629", "\u0627\u0646\u062c\u0644\u0648\u0631"))
        {
            return "frontend";
        }

        return topic;
    }

    private static string OverrideAction(string lower, string action)
    {
        if (LooksLikeResearchTask(lower, lower))
        {
            return "search";
        }

        if (ContainsAny(lower, "fix", "bug", "broken", "issue", "weak", "improve", "enhance") ||
            ContainsAny(lower, "\u062d\u0633\u0646", "\u0645\u0634\u0643\u0644\u0629", "\u0636\u0639\u064a\u0641", "\u0639\u0637\u0644\u0627\u0646"))
        {
            return "fix";
        }

        if (ContainsAny(lower, "build", "create", "implement", "add") ||
            ContainsAny(lower, "\u0627\u0628\u0646\u064a", "\u0646\u0641\u0630", "\u0627\u0639\u0645\u0644"))
        {
            return "build";
        }

        if (ContainsAny(lower, "train", "learn") || ContainsAny(lower, "\u062f\u0631\u0628", "\u062a\u062f\u0631\u064a\u0628"))
        {
            return "train";
        }

        return action;
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsWholeWord(string value, string word) =>
        Regex.IsMatch(value, $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(word)}(?![\p{{L}}\p{{N}}_])", RegexOptions.IgnoreCase);

    private static bool LooksLikeGenericCodeGeneration(string lower, string text) =>
        ContainsAny(lower, "code", "c#", "csharp", ".net", "dotnet", "method", "meethod", "methd", "function", "class", "snippet") ||
        ContainsAny(text, "\u0643\u0648\u062f", "\u0645\u064a\u062b\u0648\u062f", "\u0645\u064a\u062b\u062f", "\u062f\u0627\u0644\u0629", "\u0643\u0644\u0627\u0633", "\u0633\u064a \u0634\u0627\u0631\u0628");

    private static bool LooksLikeProjectBoundTask(string lower, string text) =>
        ContainsAny(lower, "workspace", "project", "repo", "file", "src/", "tests/", "program.cs", ".cs", ".ts", ".html", ".scss", ".json", ".md", ".csproj", ".sln", "backend", "frontend", "angular", "component", "api", "endpoint", "controller", "route", "swagger", "bug", "fix", "refactor", "model", "llm", "train", "neural", "reasoning", "brain") ||
        ContainsWholeWord(lower, "ui") ||
        ContainsAny(text, "\u0645\u0634\u0631\u0648\u0639", "\u0645\u0644\u0641", "\u0628\u0627\u0643", "\u0641\u0631\u0648\u0646\u062a", "\u0648\u0627\u062c\u0647\u0629", "\u0645\u0648\u062f\u064a\u0644", "\u062a\u062f\u0631\u064a\u0628", "\u0639\u0635\u0628\u064a", "\u0641\u064a \u0627\u0644\u0645\u0634\u0631\u0648\u0639", "\u062f\u0627\u062e\u0644 \u0627\u0644\u0645\u0634\u0631\u0648\u0639", "\u0641\u064a \u0627\u0644\u0645\u0644\u0641");

    private static bool HasExplicitToolSignal(string lower, string text) =>
        LooksLikeProjectBoundTask(lower, text) ||
        LooksLikeResearchTask(lower, text);

    private static bool LooksLikeResearchTask(string lower, string text) =>
        ContainsAny(lower, ResearchTerms) &&
        !LooksLikeLocalSearchScope(lower, text);

    private static bool LooksLikeLocalSearchScope(string lower, string text) =>
        ContainsAny(lower, LocalSearchScopeTerms) ||
        ContainsAny(text, "\u062c\u0648\u0647 \u0627\u0644\u0645\u0634\u0631\u0648\u0639", "\u062f\u0627\u062e\u0644 \u0627\u0644\u0645\u0634\u0631\u0648\u0639", "\u0641\u064a \u0627\u0644\u0645\u0634\u0631\u0648\u0639", "\u0641\u064a \u0627\u0644\u0645\u0644\u0641");

    private static bool IsCasualChat(string lower, string text)
    {
        var normalized = lower.Trim().Trim('.', '!', '?');
        return normalized is "hi" or "hello" or "hey" or "yo" or "sup" or "thanks" or "thank you" ||
               IsUnderstandingCheck(lower, text) ||
               ContainsAny(text, "\u0627\u0647\u0644\u0627", "\u0623\u0647\u0644\u0627", "\u0645\u0631\u062d\u0628\u0627", "\u0633\u0644\u0627\u0645", "\u0627\u0632\u064a\u0643", "\u0639\u0627\u0645\u0644 \u0627\u064a\u0647", "\u0634\u0643\u0631\u0627");
    }

    private static bool IsUnderstandingCheck(string lower, string text) =>
        ContainsAny(lower, "do you understand me", "understand me", "are you following", "got me") ||
        ContainsAny(text, "\u0627\u0646\u062a \u0641\u0627\u0647\u0645\u0646\u064a", "\u0623\u0646\u062a \u0641\u0627\u0647\u0645\u0646\u064a", "\u0641\u0627\u0647\u0645\u0646\u064a", "\u0641\u0647\u0645\u062a\u0646\u064a", "\u0641\u0627\u0647\u0645 \u0643\u0644\u0627\u0645\u064a");

    private static bool HasArabic(string text) => text.Any(c => c >= 0x0600 && c <= 0x06FF);

    private static string SingleLine(string value, int maxLength)
    {
        var line = string.Join(' ', value.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= maxLength ? line : line[..maxLength].TrimEnd() + "...";
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
        "have",
        "what",
        "when",
        "where",
        "want",
        "need",
        "please",
        "project",
        "workspace",
        "\u0627\u0644\u0644\u064a",
        "\u0627\u0646\u0627",
        "\u0639\u0627\u064a\u0632",
        "\u0645\u0646",
        "\u0641\u064a",
        "\u0639\u0644\u0649"
    ];

    private sealed record CognitiveFrame(
        string Text,
        BrainSignal Signal,
        IReadOnlyList<string> Clauses,
        IReadOnlyList<string> FocusTerms,
        IReadOnlyList<string> Constraints,
        string QuestionKind,
        bool HasImage);

    private sealed record CognitiveCritique(IReadOnlyList<string> Issues, double Score);

    private sealed record TrainingExample(string Label, string Text);

    private sealed record TrainedPattern(string Label, IReadOnlyList<double> Vector);
}

internal sealed record BrainSignal(
    string Topic,
    string Action,
    string Language,
    double Confidence,
    string Summary,
    IReadOnlyList<string> Keywords,
    bool RequiresTools);

internal sealed record ObservationInsights(
    IReadOnlyList<string> Routes,
    IReadOnlyList<string> ToolNames,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Symbols,
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> Evidence);
