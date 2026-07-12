using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Thoth.Core.Agent;
using Thoth.Core.Chat;
using Thoth.Core.Understanding;

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
        "typescript",
        "type script",
        "javascript",
        "java script",
        "c++",
        "cpp",
        "c plus plus",
        "ts",
        "js",
        "\u0643\u0648\u062f",
        "\u0645\u064a\u062b\u0648\u062f",
        "\u0645\u064a\u062b\u062f",
        "\u062f\u0627\u0644\u0629",
        "\u0643\u0644\u0627\u0633",
        "\u0633\u064a \u0634\u0627\u0631\u0628",
        "\u0633\u064a \u0628\u0644\u0633 \u0628\u0644\u0633"
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
        var content = request.Purpose switch
        {
            ModelRequestPurpose.UnderstandUser => CreateUnderstanding(request),
            ModelRequestPurpose.AgentPlan => CreatePlan(request),
            ModelRequestPurpose.AgentDecision => CreateAgentDecision(request),
            ModelRequestPurpose.FinalSynthesis => CreateFinalAnswer(request),
            ModelRequestPurpose.DirectReply => CreateDirectReply(request),
            ModelRequestPurpose.TrainingProbe => "The local training probe is available only through explicit training and evaluation commands.",
            _ => CreateDirectReply(request)
        };

        return Task.FromResult(new ChatResponse(content, "thoth-self"));
    }

    private static string CreatePlan(ChatRequest request)
    {
        var input = request.Input as AgentPlanModelInput;
        var goal = input?.Request.Goal ??
                   request.Messages.LastOrDefault(message => message.Role == ChatRole.User)?.Content ??
                   string.Empty;
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

    private static string CreateAgentDecision(ChatRequest request)
    {
        if (request.Input is not AgentDecisionModelInput input)
        {
            return AgentFinalDecision(
                "The agent decision request was missing structured input.",
                "I could not choose a safe tool action because the decision context was incomplete.");
        }

        var goal = input.Request.Goal;
        var observations = FormatObservations(input.Observations);
        var tools = input.Tools.Select(tool => tool.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedTools = input.Observations.Select(observation => observation.Tool).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var readPaths = input.Observations
            .Where(observation => observation.Tool.Equals("file.read", StringComparison.OrdinalIgnoreCase))
            .Select(observation => observation.Metadata is not null && observation.Metadata.TryGetValue("path", out var path) ? path.Replace('\\', '/') : string.Empty)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
                BuildFinalSynthesis(new FinalSynthesisModelInput(goal, string.Empty, input.Observations)).Content);
        }

        return AgentFinalDecision(
            "No more useful safe action was available; answer from the evidence already collected.",
            BuildFinalSynthesis(new FinalSynthesisModelInput(goal, string.Empty, input.Observations)).Content);
    }

    private static string CreateUnderstanding(ChatRequest request)
    {
        var input = request.Input as UnderstandingModelInput;
        var message = input?.Text ??
                      request.Messages.LastOrDefault(message => message.Role == ChatRole.User)?.Content ??
                      string.Empty;
        var signal = LocalSemanticBrain.AnalyzeGoal(message);
        var lower = message.ToLowerInvariant();
        var hasArabic = HasArabic(message);
        var selfAssessment = LooksLikeSelfAssessment(lower, message);
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

        var topic = selfAssessment ? "general" :
            researchTask ? "research" :
            codeGeneration && !requiresTools ? "coding" :
            modelTask ? "model" :
            frontendTask ? "frontend" :
            backendWorkspaceTask || LooksLikeBackendTask(lower) && projectBound ? "backend" :
            architectureTask ? "architecture" :
            signal.Topic != "general" ? signal.Topic :
            requiresTools ? "workspace" : "general";

        var payload = new
        {
            intent = selfAssessment ? "general_chat" : researchTask ? "research" : requiresTools ? "workspace_task" : codeGeneration ? "code_generation" : "general_chat",
            topic,
            language = hasArabic ? "ar" : "en",
            requiresTools,
            requiresVision = input?.AttachmentContentTypes.Any(type => type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) == true,
            isLongContext = message.Length > 8000,
            confidence = researchTask ? Math.Max(signal.Confidence, 0.86) : requiresTools ? Math.Max(signal.Confidence, 0.84) : codeGeneration ? Math.Max(signal.Confidence, 0.82) : signal.Confidence,
            summary = signal.Summary
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string CreateFinalAnswer(ChatRequest request)
    {
        if (request.Input is not FinalSynthesisModelInput input)
        {
            return AssistantOutputSanitizer.Sanitize(new AssistantResponse(
                AssistantResponseKind.Error,
                "I could not produce a grounded final answer because no structured observations were provided.")).Content;
        }

        return AssistantOutputSanitizer.Sanitize(BuildFinalSynthesis(input)).Content;
    }

    private static string CreateDirectReply(ChatRequest request)
    {
        var direct = request.Input as DirectReplyModelInput;
        var lastUser = request.Messages.LastOrDefault(message => message.Role == ChatRole.User);
        var text = direct?.Text ?? lastUser?.Content.Trim() ?? string.Empty;
        var hasImage = direct?.HasImage ??
                       lastUser?.Attachments?.Any(attachment => attachment.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) == true;
        return AssistantOutputSanitizer.Sanitize(UsefulResponseFallback.CreateDirectReply(text, hasImage)).Content.Trim();
    }

    private static AssistantResponse BuildFinalSynthesis(FinalSynthesisModelInput input)
    {
        var successful = input.Observations.Where(observation => observation.Succeeded).ToArray();
        if (successful.Length == 0)
        {
            return new AssistantResponse(
                AssistantResponseKind.CapabilityLimitation,
                $"I could not verify the request from tool evidence yet. Goal: {SingleLine(input.Goal, 180)}");
        }

        var builder = new StringBuilder();
        builder.AppendLine("I checked the available evidence and here is the useful summary:");
        foreach (var observation in successful.Take(8))
        {
            builder.AppendLine($"- {CleanToolName(observation.Tool)}: {SingleLine(observation.Summary, 220)}");
        }

        var failed = input.Observations.Where(observation => !observation.Succeeded).Take(3).ToArray();
        if (failed.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Some checks did not complete:");
            foreach (var observation in failed)
            {
                builder.AppendLine($"- {CleanToolName(observation.Tool)}: {SingleLine(observation.Summary, 180)}");
            }
        }

        return new AssistantResponse(AssistantResponseKind.ToolResultSummary, builder.ToString().Trim());
    }

    private static string FormatObservations(IReadOnlyList<AgentObservation> observations)
    {
        var builder = new StringBuilder();
        foreach (var observation in observations)
        {
            builder.AppendLine($"Step {observation.Step}");
            builder.AppendLine($"Tool {observation.Tool}");
            builder.AppendLine($"Succeeded {observation.Succeeded}");
            builder.AppendLine(observation.Summary);
            if (observation.Metadata is not null && observation.Metadata.Count > 0)
            {
                foreach (var metadata in observation.Metadata.Take(10))
                {
                    builder.AppendLine($"{metadata.Key}={metadata.Value}");
                }
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string CleanToolName(string tool) =>
        tool.Equals("none", StringComparison.OrdinalIgnoreCase) ? "no tool" : tool;

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
        ContainsAny(lower, "model", "llm", "reasoning model", "neural model", "train model", "local model", "checkpoint") ||
        ContainsAny(text, "\u0645\u0648\u062f\u064a\u0644", "\u064a\u0641\u0643\u0631", "\u0630\u0643\u064a", "\u0639\u0642\u0644", "\u062a\u062f\u0631\u064a\u0628", "\u0639\u0635\u0628\u064a");

    private static bool LooksLikeSelfAssessment(string lower, string text) =>
        ContainsAny(lower, "do you think you are smarter", "are you smarter", "do you think", "are you intelligent") ||
        ContainsAny(text, "\u0628\u0642\u064a\u062a \u0627\u0630\u0643\u0649", "\u0627\u0646\u062a \u0627\u0630\u0643\u0649", "\u0627\u0646\u062a \u0630\u0643\u064a");

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
