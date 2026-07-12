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
        "\u0641\u0631\u0648\u0646\u062a",
        "\u0648\u0627\u062c\u0647\u0629",
        "\u0628\u0627\u0643",
        "\u0627\u0646\u062c\u0644\u0648\u0631",
        "\u0645\u0648\u062f\u064a\u0644",
        "\u062a\u062f\u0631\u064a\u0628",
        "\u0639\u0635\u0628\u064a"
    ];

    private static readonly string[] CodeGenerationTerms =
    [
        "code",
        "c#",
        "csharp",
        ".net",
        "dotnet",
        "method",
        "meethod",
        "methd",
        "function",
        "class",
        "snippet",
        "\u0643\u0648\u062f",
        "\u0645\u064a\u062b\u0648\u062f",
        "\u0645\u064a\u062b\u062f",
        "\u062f\u0627\u0644\u0629",
        "\u0643\u0644\u0627\u0633",
        "\u0633\u064a \u0634\u0627\u0631\u0628"
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
        ".net",
        "c#",
        "csharp",
        "method",
        "function",
        "class",
        "service",
        "asp.net",
        "\u0628\u0627\u0643",
        "\u0645\u064a\u062b\u0648\u062f",
        "\u0645\u064a\u062b\u062f",
        "\u062f\u0627\u0644\u0629",
        "\u0643\u0644\u0627\u0633"
    ];

    private static readonly string[] FrontendTerms =
    [
        "frontend",
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

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var transcript = string.Join("\n", request.Messages.Select(message => message.Content));
        var lastUserContent = request.Messages.LastOrDefault(message => message.Role == ChatRole.User)?.Content ?? transcript;
        var content = IsAgentDecisionPrompt(lastUserContent)
            ? CreateAgentDecision(lastUserContent)
            : transcript.Contains("Observations:", StringComparison.OrdinalIgnoreCase)
                ? CreateFinalAnswer(transcript)
                : IsPlanPrompt(lastUserContent)
                ? CreatePlan(lastUserContent)
                : IsUnderstandingPrompt(lastUserContent)
                    ? CreateUnderstanding(lastUserContent)
                    : CreateDirectReply(request);

        return Task.FromResult(new ChatResponse(content, "thoth-self"));
    }

    private static bool IsAgentDecisionPrompt(string content) =>
        content.TrimStart().StartsWith("Return one JSON agent decision only", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlanPrompt(string content) =>
        content.TrimStart().StartsWith("Return a JSON plan only", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnderstandingPrompt(string content) =>
        content.TrimStart().StartsWith("Classify the user's message", StringComparison.OrdinalIgnoreCase);

    private static string CreatePlan(string transcript)
    {
        var goal = ExtractLineAfterLabel(transcript, "Goal:");
        var signal = LocalSemanticBrain.AnalyzeGoal(goal);
        var lower = goal.ToLowerInvariant();
        var researchTask = LooksLikeResearchTask(lower, goal);
        var steps = new List<Dictionary<string, object?>>
        {
            Step("Check internal memory for relevant prior context.", "memory.search", new Dictionary<string, string?>
            {
                ["query"] = goal,
                ["limit"] = "6"
            })
        };

        if (researchTask)
        {
            steps.Add(Step("Search the public web, read top sources, and summarize with URLs.", "web.research", new Dictionary<string, string?>
            {
                ["query"] = ExtractResearchQuery(goal),
                ["maxResults"] = "8",
                ["maxPages"] = "3"
            }));
        }
        else
        {
            steps.Add(Step("Build a project-level summary before deeper inspection.", "workspace.summary", new Dictionary<string, string?>
            {
                ["maxEntries"] = "60"
            }));
            steps.Add(Step("Map the workspace to understand the current project shape.", "workspace.map", new Dictionary<string, string?>
            {
                ["maxDepth"] = "5"
            }));
        }

        if (researchTask)
        {
            var researchPayload = new Dictionary<string, object?>
            {
                ["summary"] = "Self-contained web research plan generated by Thoth.",
                ["steps"] = steps
            };

            return JsonSerializer.Serialize(researchPayload);
        }

        var path = ExtractLikelyPath(goal);
        if (!string.IsNullOrWhiteSpace(path))
        {
            steps.Add(Step("Read the file explicitly mentioned by the user.", "file.read", new Dictionary<string, string?>
            {
                ["path"] = path,
                ["maxChars"] = "30000"
            }));
        }

        if (LooksLikeBackendTask(lower) || signal.Topic == "backend")
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

        if (LooksLikeFrontendTask(lower) || signal.Topic == "frontend")
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

        if (signal.Topic == "model" || signal.Action == "train")
        {
            steps.Add(Step("Read the self-contained reasoning model.", "file.read", new Dictionary<string, string?>
            {
                ["path"] = "src/Thoth.Llm/Models/SelfContainedReasoningModel.cs",
                ["maxChars"] = "60000"
            }));
            steps.Add(Step("Read the local semantic brain.", "file.read", new Dictionary<string, string?>
            {
                ["path"] = "src/Thoth.Llm/Models/LocalSemanticBrain.cs",
                ["maxChars"] = "60000"
            }));
            steps.Add(Step("Read the chat orchestrator.", "file.read", new Dictionary<string, string?>
            {
                ["path"] = "src/Thoth.Core/Conversations/ChatOrchestrator.cs",
                ["maxChars"] = "30000"
            }));
            steps.Add(Step("Read the agent engine.", "file.read", new Dictionary<string, string?>
            {
                ["path"] = "src/Thoth.Core/Agent/AgentEngine.cs",
                ["maxChars"] = "35000"
            }));
            steps.Add(Step("Read model regression tests.", "file.read", new Dictionary<string, string?>
            {
                ["path"] = "tests/Thoth.Tests/Core/SelfContainedReasoningModelTests.cs",
                ["maxChars"] = "30000"
            }));
        }

        if (LooksLikeArchitectureTask(lower) || signal.Topic == "architecture")
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

    private static string CreateAgentDecision(string transcript)
    {
        var goal = ExtractLineAfterLabel(transcript, "Goal:");
        var observations = ExtractBetweenLabels(transcript, "Executed observations:", "Rules:");
        var tools = ExtractAvailableTools(transcript);
        var usedTools = ExtractUsedTools(transcript).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var readPaths = ExtractReadPaths(transcript).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var signal = LocalSemanticBrain.AnalyzeGoal(goal);
        var lower = goal.ToLowerInvariant();

        if (NeedsWebResearch(lower, goal) && tools.Contains("web.research") && !usedTools.Contains("web.research"))
        {
            return AgentToolDecision(
                "Need fresh public-web evidence before answering.",
                "web.research",
                new Dictionary<string, string?>
                {
                    ["query"] = ExtractResearchQuery(goal),
                    ["maxResults"] = "8",
                    ["maxPages"] = "3"
                });
        }

        if (tools.Contains("memory.search") && !usedTools.Contains("memory.search"))
        {
            return AgentToolDecision(
                "Recall relevant project memory before choosing local evidence.",
                "memory.search",
                new Dictionary<string, string?>
                {
                    ["query"] = goal,
                    ["limit"] = "6"
                });
        }

        if (!NeedsWebResearch(lower, goal) &&
            tools.Contains("workspace.summary") &&
            !usedTools.Contains("workspace.summary"))
        {
            return AgentToolDecision(
                "Build a workspace-level frame before inspecting files.",
                "workspace.summary",
                new Dictionary<string, string?>
                {
                    ["maxEntries"] = "80"
                });
        }

        if (!NeedsWebResearch(lower, goal) &&
            tools.Contains("workspace.map") &&
            !usedTools.Contains("workspace.map"))
        {
            return AgentToolDecision(
                "Map the project so the next read is targeted.",
                "workspace.map",
                new Dictionary<string, string?>
                {
                    ["maxDepth"] = "5",
                    ["maxEntries"] = "280"
                });
        }

        var explicitPath = ExtractLikelyPath(goal);
        if (!string.IsNullOrWhiteSpace(explicitPath) &&
            tools.Contains("file.read") &&
            !readPaths.Contains(explicitPath))
        {
            return AgentToolDecision(
                "Read the file named directly by the user.",
                "file.read",
                new Dictionary<string, string?>
                {
                    ["path"] = explicitPath,
                    ["maxChars"] = "60000"
                });
        }

        var targetedReads = BuildTargetedReads(signal, lower);
        foreach (var path in targetedReads)
        {
            if (tools.Contains("file.read") && !readPaths.Contains(path))
            {
                return AgentToolDecision(
                    $"Read {path} because it is central to the {signal.Topic}/{signal.Action} request.",
                    "file.read",
                    new Dictionary<string, string?>
                    {
                        ["path"] = path,
                        ["maxChars"] = "60000"
                    });
            }
        }

        if (tools.Contains("file.search") &&
            !usedTools.Contains("file.search") &&
            signal.Topic is "model" or "backend" or "frontend" or "workspace" or "debugging")
        {
            return AgentToolDecision(
                "Search for the strongest remaining concept before final synthesis.",
                "file.search",
                new Dictionary<string, string?>
                {
                    ["query"] = ExtractSearchQuery(goal),
                    ["maxResults"] = "50"
                });
        }

        if (HasEnoughEvidence(observations, signal))
        {
            return AgentFinalDecision(
                "Collected enough evidence for a grounded final answer.",
                LocalSemanticBrain.BuildFinalAnswer(goal, observations));
        }

        return AgentFinalDecision(
            "No more useful safe action was available; answer from the evidence already collected.",
            LocalSemanticBrain.BuildFinalAnswer(goal, observations));
    }

    private static string CreateUnderstanding(string transcript)
    {
        var message = ExtractAfterLabel(transcript, "User message:");
        var signal = LocalSemanticBrain.AnalyzeGoal(message);
        var lower = message.ToLowerInvariant();
        var hasArabic = HasArabic(message);
        var codeGeneration = LooksLikeGenericCodeGeneration(lower, message);
        var researchTask = LooksLikeResearchTask(lower, message);
        var projectBound = !researchTask && (LooksLikeProjectBoundTask(lower, message) || LooksLikeFileTask(lower) || LooksLikeCommandTask(lower));
        var backendWorkspaceTask = LooksLikeBackendWorkspaceTask(lower, message);
        var frontendTask = LooksLikeFrontendTask(lower);
        var architectureTask = LooksLikeArchitectureTask(lower);
        var modelTask = LooksLikeModelTask(lower, message);
        var requiresTools = researchTask ||
                            projectBound ||
                            backendWorkspaceTask ||
                            frontendTask ||
                            architectureTask ||
                            signal.RequiresTools && !(codeGeneration && !projectBound);

        var topic = researchTask ? "research" :
            codeGeneration && !requiresTools ? "coding" :
            modelTask ? "model" :
            frontendTask ? "frontend" :
            backendWorkspaceTask || LooksLikeBackendTask(lower) && projectBound ? "backend" :
            architectureTask ? "architecture" :
            signal.Topic != "general" ? signal.Topic :
            requiresTools ? "workspace" : "general";

        var payload = new
        {
            intent = researchTask ? "research" : requiresTools ? "workspace_task" : codeGeneration ? "code_generation" : "general_chat",
            topic,
            language = hasArabic ? "ar" : "en",
            requiresTools,
            requiresVision = transcript.Contains("image/", StringComparison.OrdinalIgnoreCase),
            isLongContext = message.Length > 8000,
            confidence = researchTask ? Math.Max(signal.Confidence, 0.86) : requiresTools ? Math.Max(signal.Confidence, 0.84) : codeGeneration ? Math.Max(signal.Confidence, 0.82) : signal.Confidence,
            summary = signal.Summary
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string CreateFinalAnswer(string transcript)
    {
        var goalEndLabel = transcript.Contains("Plan:", StringComparison.OrdinalIgnoreCase)
            ? "Plan:"
            : transcript.Contains("Stop reason:", StringComparison.OrdinalIgnoreCase)
                ? "Stop reason:"
                : "Observations:";
        var goal = ExtractBetweenLabels(transcript, "Goal:", goalEndLabel);
        var observations = ExtractBetweenLabels(transcript, "Observations:", "Write a direct");
        return LocalSemanticBrain.BuildFinalAnswer(goal, observations);
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

        if (LooksLikeGenericCodeGeneration(lower, text) &&
            !LooksLikeResearchTask(lower, text) &&
            !LooksLikeProjectBoundTask(lower, text) &&
            !LooksLikeFileTask(lower) &&
            !LooksLikeCommandTask(lower))
        {
            return BuildCleanGenericCodeReply(arabic);
        }

        return LocalSemanticBrain.BuildDirectReply(text, hasImage);
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
        var lineValue = lineEnd < 0
            ? text[valueStart..].Trim()
            : text[valueStart..lineEnd].Trim();

        if (!string.IsNullOrWhiteSpace(lineValue))
        {
            return lineValue;
        }

        var remaining = lineEnd < 0 ? string.Empty : text[(lineEnd + 1)..];
        foreach (var line in remaining.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.EndsWith(':'))
            {
                break;
            }

            return line.Trim();
        }

        return string.Empty;
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

    private static IReadOnlySet<string> ExtractAvailableTools(string transcript)
    {
        var tools = Regex.Matches(transcript, @"^\s*-\s*(?<tool>[\w.]+):", RegexOptions.Multiline)
            .Select(match => match.Groups["tool"].Value)
            .Where(tool => tool.Contains('.', StringComparison.Ordinal))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return tools;
    }

    private static IEnumerable<string> ExtractUsedTools(string transcript) =>
        Regex.Matches(transcript, @"Tool:\s*(?<tool>[\w.]+)", RegexOptions.IgnoreCase)
            .Select(match => match.Groups["tool"].Value)
            .Where(tool => !string.IsNullOrWhiteSpace(tool));

    private static IEnumerable<string> ExtractReadPaths(string transcript) =>
        Regex.Matches(transcript, @"Tool:\s*file\.read\s+\{[^\n]*""path""\s*:\s*""(?<path>[^""]+)""", RegexOptions.IgnoreCase)
            .Select(match => match.Groups["path"].Value.Replace('\\', '/'))
            .Where(path => !string.IsNullOrWhiteSpace(path));

    private static IReadOnlyList<string> BuildTargetedReads(BrainSignal signal, string lowerGoal)
    {
        if (signal.Topic == "model" ||
            ContainsAny(lowerGoal, "thinking", "reasoning", "brain", "decision", "understand", "planner") ||
            ContainsAny(lowerGoal, "\u064a\u0641\u0643\u0631", "\u062a\u0641\u0643\u064a\u0631", "\u0639\u0642\u0644", "\u064a\u0641\u0647\u0645"))
        {
            return
            [
                "src/Thoth.Llm/Models/SelfContainedReasoningModel.cs",
                "src/Thoth.Llm/Models/LocalSemanticBrain.cs",
                "src/Thoth.Core/Agent/ModelAgentDecisionService.cs",
                "src/Thoth.Core/Agent/AgentEngine.cs",
                "src/Thoth.Core/Understanding/HeuristicUnderstandingService.cs",
                "src/Thoth.Core/Conversations/ChatOrchestrator.cs"
            ];
        }

        if (signal.Topic == "backend")
        {
            return
            [
                "src/Thoth.Api/Program.cs",
                "src/Thoth.Api/Contracts/ApiContracts.cs",
                "src/Thoth.Core/Conversations/ChatOrchestrator.cs"
            ];
        }

        if (signal.Topic == "frontend")
        {
            return
            [
                "src/Thoth.Web/src/app/app.ts",
                "src/Thoth.Web/src/app/app.html",
                "src/Thoth.Web/src/app/app.scss"
            ];
        }

        return [];
    }

    private static bool HasEnoughEvidence(string observations, BrainSignal signal)
    {
        var successfulReads = Regex.Matches(observations, @"Tool:\s*file\.read", RegexOptions.IgnoreCase).Count;
        var successfulWebResearch = observations.Contains("Tool: web.research", StringComparison.OrdinalIgnoreCase) &&
                                    observations.Contains("Succeeded: True", StringComparison.OrdinalIgnoreCase);

        if (signal.Topic == "research")
        {
            return successfulWebResearch || observations.Contains("URL:", StringComparison.OrdinalIgnoreCase);
        }

        if (signal.Topic == "model")
        {
            return successfulReads >= 3 &&
                   observations.Contains("SelfContainedReasoningModel", StringComparison.OrdinalIgnoreCase) &&
                   observations.Contains("LocalSemanticBrain", StringComparison.OrdinalIgnoreCase);
        }

        return successfulReads > 0 ||
               observations.Contains("workspace.summary", StringComparison.OrdinalIgnoreCase) &&
               observations.Length > 1200;
    }

    private static bool NeedsWebResearch(string lower, string text) =>
        LooksLikeResearchTask(lower, text);

    private static string AgentToolDecision(
        string rationale,
        string tool,
        IReadOnlyDictionary<string, string?> arguments) =>
        JsonSerializer.Serialize(new
        {
            kind = "tool",
            rationale,
            tool,
            arguments
        });

    private static string AgentFinalDecision(string rationale, string answer) =>
        JsonSerializer.Serialize(new
        {
            kind = "final",
            rationale,
            answer
        });

    private static string ExtractResearchQuery(string goal)
    {
        var cleaned = goal.Trim();
        cleaned = Regex.Replace(cleaned, @"\b(search|look\s*up|lookup|research|summarize|summary|web|internet|online|google|latest|current)\b", " ", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\b(the|for|and|it|them|about|please|on)\b", " ", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, "(\u0627\u0628\u062d\u062b|\u0628\u062d\u062b|\u062f\u0648\u0631|\u0627\u0644\u0646\u062a|\u062c\u0648\u062c\u0644|\u0648\u064a\u0628|\u0644\u062e\u0635|\u0645\u0644\u062e\u0635|\u0627\u062d\u062f\u062b|\u0623\u062d\u062f\u062b|\u0627\u062e\u0631|\u0622\u062e\u0631|\u062d\u0627\u0644\u064a|\u0627\u062e\u0628\u0627\u0631|\u0623\u062e\u0628\u0627\u0631)", " ", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? goal.Trim() : cleaned;
    }

    private static bool LooksLikeBackendTask(string lower) =>
        ContainsAny(lower, BackendTerms);

    private static bool LooksLikeBackendWorkspaceTask(string lower, string text) =>
        ContainsAny(lower, "backend", "api", "endpoint", "http", "controller", "route", "server", "swagger", "asp.net", "program.cs") ||
        ContainsAny(text, "\u0628\u0627\u0643");

    private static bool LooksLikeFrontendTask(string lower) =>
        ContainsAny(lower, FrontendTerms) || ContainsWholeWord(lower, "ui");

    private static bool LooksLikeArchitectureTask(string lower) =>
        ContainsAny(lower, ArchitectureTerms);

    private static bool LooksLikeModelTask(string lower, string text) =>
        ContainsAny(lower, "model", "llm", "reason", "reasoning", "neural", "train", "brain", "think", "intelligence") ||
        ContainsAny(text, "\u0645\u0648\u062f\u064a\u0644", "\u064a\u0641\u0643\u0631", "\u0630\u0643\u064a", "\u0639\u0642\u0644", "\u062a\u062f\u0631\u064a\u0628", "\u0639\u0635\u0628\u064a");

    private static bool LooksLikeResearchTask(string lower, string text) =>
        ContainsAny(lower, ResearchTerms) &&
        !LooksLikeLocalSearchScope(lower, text);

    private static bool LooksLikeLocalSearchScope(string lower, string text) =>
        ContainsAny(lower, LocalSearchScopeTerms) ||
        ContainsAny(text, "\u062c\u0648\u0647 \u0627\u0644\u0645\u0634\u0631\u0648\u0639", "\u062f\u0627\u062e\u0644 \u0627\u0644\u0645\u0634\u0631\u0648\u0639", "\u0641\u064a \u0627\u0644\u0645\u0634\u0631\u0648\u0639", "\u0641\u064a \u0627\u0644\u0645\u0644\u0641");

    private static bool LooksLikeGenericCodeGeneration(string lower, string text) =>
        ContainsAny(lower, CodeGenerationTerms) ||
        ContainsAny(text, "\u0633\u064a \u0634\u0627\u0631\u0628");

    private static bool LooksLikeProjectBoundTask(string lower, string text) =>
        ContainsAny(lower, "workspace", "project", "repo", "file", "src/", "tests/", "program.cs", "api", "endpoint", "backend", "frontend", "angular", "component", "controller", "route", "swagger", "bug", "fix", "refactor", "model", "llm", "train", "neural", "reasoning", "brain") ||
        ContainsWholeWord(lower, "ui") ||
        ContainsAny(text, ArabicWorkspaceTerms) ||
        ContainsAny(text, "\u0641\u064a \u0627\u0644\u0645\u0634\u0631\u0648\u0639", "\u062f\u0627\u062e\u0644 \u0627\u0644\u0645\u0634\u0631\u0648\u0639", "\u0641\u064a \u0627\u0644\u0645\u0644\u0641");

    private static bool LooksLikeFileTask(string lower) =>
        ContainsAny(lower, ".cs", ".ts", ".html", ".scss", ".json", ".md", ".csproj", ".sln");

    private static bool LooksLikeCommandTask(string lower) =>
        lower.StartsWith("run ", StringComparison.OrdinalIgnoreCase) ||
        ContainsAny(lower, "dotnet ", "npm ", "ng ");

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsWholeWord(string value, string word) =>
        Regex.IsMatch(value, $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(word)}(?![\p{{L}}\p{{N}}_])", RegexOptions.IgnoreCase);

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
