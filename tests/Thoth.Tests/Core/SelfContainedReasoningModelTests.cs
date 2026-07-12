using System.Text.Json;
using Thoth.Core.Agent;
using Thoth.Core.Chat;
using Thoth.Core.Memory;
using Thoth.Core.Tools;
using Thoth.Llm.Models;

namespace Thoth.Tests.Core;

public sealed class SelfContainedReasoningModelTests
{
    [Fact]
    public async Task DirectReply_ArabicTypeScriptMethodAsksForClarificationWithoutDiagnostics()
    {
        var response = await CompleteDirectAsync("\u0645\u0645\u0643\u0646 \u062a\u0628\u0646\u064a method Type script");

        Assert.Contains("TypeScript", response.Content);
        Assert.Contains("\u0645\u0637\u0644\u0648\u0628 \u0645\u0646\u0647\u0627 \u062a\u0639\u0645\u0644 \u0625\u064a\u0647", response.Content);
        Assert.DoesNotContain("C#", response.Content);
        AssertNoInternalDiagnostics(response.Content);
    }

    [Fact]
    public async Task DirectReply_CalculatorRequestReturnsUsefulCSharpOnly()
    {
        var response = await CompleteDirectAsync("can you build C# method work as calculator");

        Assert.Contains("```csharp", response.Content);
        Assert.Contains("public static decimal Calculate", response.Content);
        Assert.Contains("DivideByZeroException", response.Content);
        Assert.DoesNotContain("ordered tasks", response.Content, StringComparison.OrdinalIgnoreCase);
        AssertNoInternalDiagnostics(response.Content);
    }

    [Fact]
    public async Task DirectReply_CasualSelfAssessmentDoesNotDumpArchitecture()
    {
        var response = await CompleteDirectAsync("Do you think you are smarter now?");

        Assert.Contains("should not claim real intelligence", response.Content);
        Assert.DoesNotContain("workspace", response.Content, StringComparison.OrdinalIgnoreCase);
        AssertNoInternalDiagnostics(response.Content);
    }

    [Fact]
    public async Task Understanding_TypedRequestDetectsGenericCodeWithoutTools()
    {
        var result = await UnderstandAsync("\u0645\u0645\u0643\u0646 \u062a\u0628\u0646\u064a method Type script");

        Assert.Equal("code_generation", result.RootElement.GetProperty("intent").GetString());
        Assert.Equal("coding", result.RootElement.GetProperty("topic").GetString());
        Assert.False(result.RootElement.GetProperty("requiresTools").GetBoolean());
        Assert.Equal("ar", result.RootElement.GetProperty("language").GetString());
    }

    [Fact]
    public async Task Understanding_TypedRequestKeepsCasualSmartQuestionOutOfTools()
    {
        var result = await UnderstandAsync("Do you think you are smarter now?");

        Assert.Equal("general_chat", result.RootElement.GetProperty("intent").GetString());
        Assert.Equal("general", result.RootElement.GetProperty("topic").GetString());
        Assert.False(result.RootElement.GetProperty("requiresTools").GetBoolean());
    }

    [Fact]
    public async Task Understanding_TypedRequestRoutesExplicitRepositoryTaskToTools()
    {
        var result = await UnderstandAsync("Fix the TypeScript method in src/app/foo.ts and run tests");

        Assert.Equal("workspace_task", result.RootElement.GetProperty("intent").GetString());
        Assert.True(result.RootElement.GetProperty("requiresTools").GetBoolean());
    }

    [Fact]
    public async Task AgentPlan_TypedWebResearchUsesResearchTool()
    {
        var model = new SelfContainedReasoningModel();
        var request = new AgentRequest(
            "search the web for LangGraph and summarize it",
            Directory.GetCurrentDirectory(),
            "thoth-self");

        var response = await model.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, request.Goal)],
            "thoth-self",
            0,
            Purpose: ModelRequestPurpose.AgentPlan,
            Input: new AgentPlanModelInput(
                request,
                [],
                [Tool("memory.search", ("query", true), ("limit", false)), Tool("web.research", ("query", true))])));

        Assert.Contains("web.research", response.Content);
        Assert.DoesNotContain("workspace.summary", response.Content);
    }

    [Fact]
    public async Task AgentDecision_TypedRequestChoosesMemoryBeforeWorkspaceInspection()
    {
        var model = new SelfContainedReasoningModel();
        var request = new AgentRequest(
            "the model is not thinking, build a real reasoning brain",
            Directory.GetCurrentDirectory(),
            "thoth-self");

        var response = await model.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, request.Goal)],
            "thoth-self",
            0,
            Purpose: ModelRequestPurpose.AgentDecision,
            Input: new AgentDecisionModelInput(
                request,
                [],
                [
                    Tool("memory.search", ("query", true), ("limit", false)),
                    Tool("workspace.summary", ("maxEntries", false)),
                    Tool("workspace.map", ("maxDepth", false)),
                    Tool("file.read", ("path", true))
                ],
                [])));

        using var json = JsonDocument.Parse(response.Content);
        Assert.Equal("tool", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("memory.search", json.RootElement.GetProperty("tool").GetString());
    }

    [Fact]
    public async Task AgentDecision_TypedRequestReadsReasoningFilesAfterWorkspaceMap()
    {
        var model = new SelfContainedReasoningModel();
        var request = new AgentRequest(
            "the model is not thinking, build a real reasoning brain",
            Directory.GetCurrentDirectory(),
            "thoth-self");
        var observations = new AgentObservation[]
        {
            new(1, "memory.search", true, "no memory"),
            new(2, "workspace.summary", true, "solution contains Thoth.Llm and Thoth.Core"),
            new(3, "workspace.map", true, "src/Thoth.Llm/Models/SelfContainedReasoningModel.cs")
        };

        var response = await model.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, request.Goal)],
            "thoth-self",
            0,
            Purpose: ModelRequestPurpose.AgentDecision,
            Input: new AgentDecisionModelInput(
                request,
                [],
                [
                    Tool("memory.search", ("query", true), ("limit", false)),
                    Tool("workspace.summary", ("maxEntries", false)),
                    Tool("workspace.map", ("maxDepth", false)),
                    Tool("file.read", ("path", true))
                ],
                observations)));

        using var json = JsonDocument.Parse(response.Content);
        Assert.Equal("tool", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("file.read", json.RootElement.GetProperty("tool").GetString());
        Assert.Equal(
            "src/Thoth.Llm/Models/SelfContainedReasoningModel.cs",
            json.RootElement.GetProperty("arguments").GetProperty("path").GetString());
    }

    [Fact]
    public async Task FinalSynthesis_TypedObservationsDoNotLeakControlPromptMarkers()
    {
        var model = new SelfContainedReasoningModel();
        var response = await model.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, "search the web for LangGraph and summarize it")],
            "thoth-self",
            0,
            Purpose: ModelRequestPurpose.FinalSynthesis,
            Input: new FinalSynthesisModelInput(
                "search the web for LangGraph and summarize it",
                "finished",
                [
                    new AgentObservation(
                        1,
                        "web.research",
                        true,
                        "LangGraph docs: https://langchain-ai.github.io/langgraph/ - stateful agent workflows.",
                        new Dictionary<string, string> { ["url"] = "https://langchain-ai.github.io/langgraph/" })
                ])));

        Assert.Contains("https://langchain-ai.github.io/langgraph/", response.Content);
        Assert.DoesNotContain("Stop reason:", response.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Observations:", response.Content, StringComparison.OrdinalIgnoreCase);
        AssertNoInternalDiagnostics(response.Content);
    }

    [Fact]
    public void Sanitizer_ReplacesInternalDiagnosticLeak()
    {
        var sanitized = AssistantOutputSanitizer.Sanitize(new AssistantResponse(
            AssistantResponseKind.DirectAnswer,
            "ordered tasks:\n1. request.atomize: something\nrevision: bad"));

        Assert.DoesNotContain("ordered tasks", sanitized.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("revision:", sanitized.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AssistantResponseKind.Error, sanitized.Kind);
    }

    private static async Task<ChatResponse> CompleteDirectAsync(string text)
    {
        var model = new SelfContainedReasoningModel();
        return await model.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, text)],
            "thoth-self",
            0,
            Purpose: ModelRequestPurpose.DirectReply,
            Input: new DirectReplyModelInput(text)));
    }

    private static async Task<JsonDocument> UnderstandAsync(string text)
    {
        var model = new SelfContainedReasoningModel();
        var response = await model.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, text)],
            "thoth-self",
            0,
            Purpose: ModelRequestPurpose.UnderstandUser,
            Input: new UnderstandingModelInput(text, [])));
        return JsonDocument.Parse(response.Content);
    }

    private static ModelToolDescriptor Tool(string name, params (string Name, bool Required)[] parameters) =>
        new(
            name,
            "Test tool.",
            parameters.Select(parameter => new ToolParameter(parameter.Name, "test", parameter.Required)).ToArray());

    private static void AssertNoInternalDiagnostics(string content)
    {
        foreach (var marker in AssistantOutputSanitizer.ForbiddenMarkers)
        {
            Assert.DoesNotContain(marker, content, StringComparison.OrdinalIgnoreCase);
        }
    }
}
