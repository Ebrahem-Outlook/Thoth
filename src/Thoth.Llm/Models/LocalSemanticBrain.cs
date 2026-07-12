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
        var evidence = RankEvidence(goal, observations)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();

        return new ObservationInsights(routes, tools, files, symbols, failures, evidence);
    }

    public static string BuildFinalAnswer(string goal, string observations)
    {
        var signal = AnalyzeGoal(goal);
        var insights = AnalyzeObservations(goal, observations);
        var builder = new StringBuilder();

        builder.AppendLine(signal.Topic == "research"
            ? signal.Language == "ar"
                ? "\u0641\u0627\u0647\u0645\u0643. \u0643\u0633\u0631\u062a \u0627\u0644\u0637\u0644\u0628\u060c \u0628\u062d\u062b\u062a \u0639\u0644\u0649 \u0627\u0644\u0648\u064a\u0628\u060c \u0648\u0642\u0631\u0623\u062a \u0627\u0644\u0645\u0635\u0627\u062f\u0631 \u0642\u0628\u0644 \u0627\u0644\u0645\u0644\u062e\u0635."
                : "I broke the request down, searched the web, read sources, and then formed the answer."
            : signal.Language == "ar"
                ? "\u0641\u0627\u0647\u0645\u0643. \u0643\u0633\u0631\u062a \u0627\u0644\u0637\u0644\u0628 \u0644\u062a\u0635\u0646\u064a\u0641 \u0645\u062d\u0644\u064a\u060c \u0648\u0641\u062d\u0635\u062a \u0627\u0644\u0645\u0644\u0641\u0627\u062a \u0648\u0627\u0644\u0623\u062f\u0648\u0627\u062a \u0642\u0628\u0644 \u0645\u0627 \u0623\u0631\u062f."
                : "I broke the request down locally, checked the workspace evidence, and then formed the answer.");
        builder.AppendLine();
        AppendUnderstanding(builder, signal);

        builder.AppendLine();
        AppendFindings(builder, signal, insights);
        AppendEvidence(builder, signal, insights);
        AppendToolSummary(builder, signal, insights);
        AppendNextMove(builder, signal, insights);

        return builder.ToString().Trim();
    }

    public static string BuildDirectReply(string text, bool hasImage)
    {
        var signal = AnalyzeGoal(text);
        var builder = new StringBuilder();

        if (IsUnderstandingCheck(text.ToLowerInvariant(), text))
        {
            return signal.Language == "ar"
                ? "\u0623\u064a\u0648\u0647 \u0641\u0627\u0647\u0645\u0643. \u0627\u0644\u0644\u064a \u0628\u062a\u0633\u0623\u0644\u0647 \u0647\u0646\u0627 \u0645\u0634 \u0645\u0647\u0645\u0629 \u0628\u0627\u0643 \u0623\u0648 \u0643\u0648\u062f\u060c \u062f\u0647 \u0633\u0624\u0627\u0644 \u0639\u0627\u062f\u064a \u0648\u0647\u0631\u062f \u0639\u0644\u064a\u0647 \u0639\u0644\u0649 \u0647\u0630\u0627 \u0627\u0644\u0623\u0633\u0627\u0633."
                : "Yes, I understand you. This is normal conversation, not a workspace task, so I should answer directly without running tools.";
        }

        if (!signal.RequiresTools)
        {
            if (signal.Topic == "coding")
            {
                return BuildCleanGenericCodeReply(signal.Language == "ar");
            }

            return signal.Language == "ar"
                ? "\u0641\u0627\u0647\u0645\u0643. \u062f\u0647 \u0643\u0644\u0627\u0645 \u0639\u0627\u062f\u064a\u060c \u0641\u0647\u0631\u062f \u0639\u0644\u064a\u0643 \u0645\u0628\u0627\u0634\u0631\u0629 \u0645\u0646 \u063a\u064a\u0631 \u062a\u0634\u063a\u064a\u0644 \u0623\u062f\u0648\u0627\u062a \u0623\u0648 \u062a\u062e\u0645\u064a\u0646 \u0625\u0646\u0647 \u0637\u0644\u0628 \u0643\u0648\u062f."
                : "I understand. This is normal conversation, so I should answer directly without running tools or pretending it is a code task.";
        }

        if (signal.Language == "ar")
        {
            builder.AppendLine(signal.Topic == "research"
                ? "\u0641\u0627\u0647\u0645\u0643. \u062f\u0647 \u0637\u0644\u0628 \u0628\u062d\u062b \u0639\u0644\u0649 \u0627\u0644\u0648\u064a\u0628\u060c \u0641\u0627\u0644\u0635\u062d \u0625\u0646\u064a \u0623\u0634\u063a\u0644 \u0623\u062f\u0627\u0629 \u0627\u0644\u0628\u062d\u062b \u0648\u0623\u0642\u0631\u0623 \u0645\u0635\u0627\u062f\u0631 \u0642\u0628\u0644 \u0645\u0627 \u0623\u0644\u062e\u0635."
                : "\u0641\u0627\u0647\u0645\u0643. \u062f\u0647 \u0628\u0627\u064a\u0646 \u0637\u0644\u0628 \u0639\u0644\u0649 \u0627\u0644\u0645\u0634\u0631\u0648\u0639\u060c \u0641\u0627\u0644\u0635\u062d \u0625\u0646\u064a \u0623\u0641\u062d\u0635 \u0627\u0644\u0645\u0644\u0641\u0627\u062a \u0648\u0627\u0644\u0623\u062f\u0648\u0627\u062a \u0642\u0628\u0644 \u0627\u0644\u0631\u062f.");
        }
        else
        {
            builder.AppendLine(signal.Topic == "research"
                ? "I understand the request. This needs web research, so I should search, read sources, and cite URLs before answering."
                : "I understand the request. This looks project-related, so I should inspect local files and tool output before answering.");
        }

        builder.AppendLine();
        builder.AppendLine($"I read it as a {signal.Topic}/{signal.Action} task with confidence {signal.Confidence:0.00}.");

        if (hasImage)
        {
            builder.AppendLine("The attachment is stored with the conversation; pixel-level interpretation still needs an internal vision module.");
        }

        builder.AppendLine(signal.Language == "ar"
            ? signal.Topic == "research"
                ? "\u0627\u0641\u062a\u062d Tools \u0648\u0647\u0627\u062f\u0648\u0631 \u0639\u0644\u0649 \u0627\u0644\u0648\u064a\u0628 \u0648\u0623\u0631\u062c\u0639\u0644\u0643 \u0628\u0645\u0644\u062e\u0635 \u0648\u0645\u0635\u0627\u062f\u0631."
                : "\u0627\u0641\u062a\u062d Tools \u0648\u0627\u062f\u064a\u0646\u064a \u0647\u062f\u0641 \u0645\u062d\u062f\u062f\u060c \u0648\u0647\u062d\u0648\u0644\u0647 \u0644\u062e\u0637\u0629 \u0648\u062a\u0646\u0641\u064a\u0630."
            : signal.Topic == "research"
                ? "Keep Tools enabled and I will search the web, read sources, and return a sourced summary."
                : "Keep Tools enabled and give me the concrete target; I will turn it into an inspect-plan-answer loop.");

        return builder.ToString().Trim();
    }

    private static string BuildCleanGenericCodeReply(bool arabic) =>
        arabic
            ? string.Join(Environment.NewLine,
                "\u0623\u0643\u064a\u062f. \u0628\u0633 \u0627\u0644\u0637\u0644\u0628 \u0646\u0627\u0642\u0635 \u0623\u0647\u0645 \u062c\u0632\u0621: \u0627\u0644\u0645\u064a\u062b\u0648\u062f \u062a\u0639\u0645\u0644 \u0625\u064a\u0647 \u0628\u0627\u0644\u0636\u0628\u0637\u061f \u0644\u062d\u062f \u0645\u0627 \u062a\u062d\u062f\u062f \u0627\u0644\u0645\u0637\u0644\u0648\u0628\u060c \u062f\u0647 \u0642\u0627\u0644\u0628 C# \u0646\u0638\u064a\u0641 \u062a\u0642\u062f\u0631 \u062a\u0628\u0646\u064a \u0639\u0644\u064a\u0647:",
                "",
                "```csharp",
                "public static bool TryNormalizeName(string? value, out string normalized)",
                "{",
                "    normalized = string.Empty;",
                "",
                "    if (string.IsNullOrWhiteSpace(value))",
                "    {",
                "        return false;",
                "    }",
                "",
                "    normalized = value.Trim();",
                "    return true;",
                "}",
                "```",
                "",
                "\u0627\u0628\u0639\u062a\u0644\u064a \u0627\u0633\u0645 \u0627\u0644\u0645\u064a\u062b\u0648\u062f\u060c \u0627\u0644\u0645\u062f\u062e\u0644\u0627\u062a\u060c \u0627\u0644\u0646\u0627\u062a\u062c \u0627\u0644\u0645\u062a\u0648\u0642\u0639\u060c \u0648\u0642\u0648\u0627\u0639\u062f \u0627\u0644\u0640 validation\u060c \u0648\u0623\u0646\u0627 \u0623\u0637\u0644\u0639\u0647\u0627 \u062c\u0627\u0647\u0632\u0629 \u0639\u0644\u0649 \u0627\u0644\u0645\u0637\u0644\u0648\u0628.")
            : """
              Sure. I need the method's purpose before I can write the exact version. Here is a clean C# template you can build from:

              ```csharp
              public static bool TryNormalizeName(string? value, out string normalized)
              {
                  normalized = string.Empty;

                  if (string.IsNullOrWhiteSpace(value))
                  {
                      return false;
                  }

                  normalized = value.Trim();
                  return true;
              }
              ```

              Send me the method name, inputs, expected return value, and validation rules, and I will write the final method.
              """;

    private static string BuildGenericCodeReply(bool arabic) =>
        arabic
            ? """
              أكيد. بس الطلب ناقص أهم جزء: الميثود تعمل إيه بالضبط؟ لحد ما تحدد المطلوب، ده قالب C# نظيف تقدر تبني عليه:

              ```csharp
              public static bool TryNormalizeName(string? value, out string normalized)
              {
                  normalized = string.Empty;

                  if (string.IsNullOrWhiteSpace(value))
                  {
                      return false;
                  }

                  normalized = value.Trim();
                  return true;
              }
              ```

              ابعتلي اسم الميثود، المدخلات، الناتج المتوقع، وقواعد الـ validation، وأنا أطلعها جاهزة على المطلوب.
              """
            : """
              Sure. I need the method's purpose before I can write the exact version. Here is a clean C# template you can build from:

              ```csharp
              public static bool TryNormalizeName(string? value, out string normalized)
              {
                  normalized = string.Empty;

                  if (string.IsNullOrWhiteSpace(value))
                  {
                      return false;
                  }

                  normalized = value.Trim();
                  return true;
              }
              ```

              Send me the method name, inputs, expected return value, and validation rules, and I will write the final method.
              """;

    private static void AppendUnderstanding(StringBuilder builder, BrainSignal signal)
    {
        builder.AppendLine(signal.Language == "ar" ? "\u0627\u0644\u0644\u064a \u0641\u0647\u0645\u062a\u0647:" : "What I understood:");
        builder.AppendLine(signal.Language == "ar"
            ? $"- \u0623\u0646\u062a \u0628\u062a\u0637\u0644\u0628: {signal.Summary}"
            : $"- You asked: {signal.Summary}");
        builder.AppendLine(signal.Language == "ar"
            ? $"- \u0627\u0644\u062a\u0635\u0646\u064a\u0641: {signal.Topic} / {signal.Action} ({signal.Confidence:0.00})"
            : $"- Local classification: {signal.Topic} / {signal.Action} ({signal.Confidence:0.00})");

        if (signal.Keywords.Count > 0)
        {
            builder.AppendLine(signal.Language == "ar"
                ? $"- \u0643\u0644\u0645\u0627\u062a \u0645\u062d\u0648\u0631\u064a\u0629: {string.Join(", ", signal.Keywords)}"
                : $"- Focus terms: {string.Join(", ", signal.Keywords)}");
        }
    }

    private static void AppendFindings(StringBuilder builder, BrainSignal signal, ObservationInsights insights)
    {
        builder.AppendLine(signal.Language == "ar" ? "\u0627\u0644\u0644\u064a \u0644\u0642\u064a\u062a\u0647:" : "What I found:");

        if (signal.Topic == "backend" && insights.Routes.Count > 0)
        {
            builder.AppendLine($"- Found {insights.Routes.Count} HTTP routes.");
            foreach (var group in GroupRoutes(insights.Routes).Take(8))
            {
                builder.AppendLine($"- {group.Key}: {string.Join(", ", group.Value.Take(8))}");
            }
        }
        else if (signal.Topic == "model")
        {
            builder.AppendLine("- The current runtime is local and self-contained; this path does not call an external LLM.");
            builder.AppendLine("- The brain layer separates casual chat from workspace tasks before the agent loop.");
            builder.AppendLine("- For project tasks it uses local pattern centroids, hashed embeddings, evidence ranking, and task-specific synthesis.");
            AppendIfAny(builder, "Relevant model symbols", insights.Symbols.Where(symbol =>
                ContainsAny(symbol, "Model", "Engine", "Planner", "Understanding", "Orchestrator")));
        }
        else if (signal.Topic == "research")
        {
            AppendIfAny(builder, "Web evidence", insights.Evidence);
        }
        else if (signal.Topic == "frontend")
        {
            AppendIfAny(builder, "Relevant UI files", insights.Files.Where(file =>
                file.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".scss", StringComparison.OrdinalIgnoreCase)));
            AppendIfAny(builder, "UI signals", insights.Evidence);
        }
        else
        {
            AppendIfAny(builder, "Evidence", insights.Evidence);
        }

        if (insights.Failures.Count > 0)
        {
            AppendIfAny(builder, "Problems noticed", insights.Failures);
        }

        var hasTopicFindings = signal.Topic == "model" ||
                               signal.Topic == "backend" && insights.Routes.Count > 0 ||
                               signal.Topic == "frontend" && insights.Files.Count > 0 ||
                               signal.Topic == "research" && insights.Evidence.Count > 0;
        if (!hasTopicFindings &&
            insights.Routes.Count == 0 &&
            insights.Evidence.Count == 0 &&
            insights.Failures.Count == 0)
        {
            builder.AppendLine("- I did not get enough useful evidence from this run. The next pass should read more targeted files.");
        }
    }

    private static void AppendEvidence(StringBuilder builder, BrainSignal signal, ObservationInsights insights)
    {
        if (signal.Topic == "model" || signal.Topic == "backend" && insights.Routes.Count > 0)
        {
            return;
        }

        var evidence = insights.Evidence
            .Where(line => !line.StartsWith("Workspace:", StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .ToArray();

        if (evidence.Length == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine(signal.Language == "ar" ? "\u0623\u0647\u0645 \u0625\u0634\u0627\u0631\u0627\u062a \u0627\u062a\u0641\u062d\u0635\u062a:" : "Most relevant evidence:");
        foreach (var line in evidence)
        {
            builder.AppendLine($"- {line}");
        }
    }

    private static void AppendToolSummary(StringBuilder builder, BrainSignal signal, ObservationInsights insights)
    {
        if (insights.ToolNames.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine(signal.Topic == "research"
            ? signal.Language == "ar" ? "\u0627\u0633\u062a\u062e\u062f\u0645\u062a \u0623\u062f\u0648\u0627\u062a:" : "Checked with tools:"
            : signal.Language == "ar" ? "\u0641\u062d\u0635\u062a \u0645\u062d\u0644\u064a\u0627:" : "Checked locally:");
        builder.AppendLine($"- {string.Join(", ", insights.ToolNames.Take(8))}");
    }

    private static void AppendNextMove(StringBuilder builder, BrainSignal signal, ObservationInsights insights)
    {
        builder.AppendLine();
        builder.AppendLine(signal.Language == "ar" ? "\u0627\u0644\u062e\u0637\u0648\u0629 \u0627\u0644\u062c\u0627\u064a\u0629:" : "Next best move:");
        var move = signal.Topic switch
        {
            "backend" when signal.Action is "fix" or "build" => "Add or update backend tests around the exact endpoint/flow, then change the route/service code.",
            "backend" => "Use the route map to pick one endpoint, inspect its request/response contract, then add a focused test.",
            "frontend" => "Inspect the Angular component state and template around the target workflow, then validate it in the browser.",
            "model" => "Train/update the local pattern bank with workspace examples and add regression tests for answer quality.",
            "research" => "Read one more high-signal source if the evidence conflicts, then cite the final source list.",
            "debugging" when insights.Failures.Count > 0 => "Start from the first failure above, reproduce it, and patch the responsible file.",
            "testing" => "Run the smallest failing test/build command, then expand coverage only around the changed behavior.",
            _ => "Give me a narrower build/fix/review target and I will route it through local tools."
        };

        builder.AppendLine($"- {move}");
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
