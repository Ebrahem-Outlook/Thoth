using Thoth.Core.Agent;
using Thoth.Core.Memory;
using Thoth.Core.Tools;
using Thoth.Core.Understanding;

namespace Thoth.Core.Chat;

public enum ModelRequestPurpose
{
    DirectReply,
    UnderstandUser,
    AgentPlan,
    AgentDecision,
    FinalSynthesis,
    TrainingProbe
}

public abstract record ModelRequestInput;

public sealed record DirectReplyModelInput(
    string Text,
    bool HasImage = false,
    string? PreferredLanguage = null,
    IReadOnlyList<ChatAttachment>? Attachments = null,
    string? ActiveTaskSummary = null) : ModelRequestInput;

public sealed record UnderstandingModelInput(
    string Text,
    IReadOnlyList<string> AttachmentContentTypes,
    string? Project = null,
    string? ActiveTaskSummary = null) : ModelRequestInput;

public sealed record AgentPlanModelInput(
    AgentRequest Request,
    IReadOnlyList<MemoryRecord> Memories,
    IReadOnlyList<ModelToolDescriptor> Tools) : ModelRequestInput;

public sealed record AgentDecisionModelInput(
    AgentRequest Request,
    IReadOnlyList<MemoryRecord> Memories,
    IReadOnlyList<ModelToolDescriptor> Tools,
    IReadOnlyList<AgentObservation> Observations) : ModelRequestInput;

public sealed record FinalSynthesisModelInput(
    string Goal,
    string StopReason,
    IReadOnlyList<AgentObservation> Observations) : ModelRequestInput;

public sealed record ModelToolDescriptor(
    string Name,
    string Description,
    IReadOnlyList<ToolParameter> Parameters)
{
    public static ModelToolDescriptor FromTool(IAgentTool tool) =>
        new(tool.Name, tool.Description, tool.Parameters);
}

public sealed record AssistantResponse(
    AssistantResponseKind Kind,
    string Content,
    IReadOnlyList<string>? SuggestedDetails = null);

public enum AssistantResponseKind
{
    DirectAnswer,
    Clarification,
    ToolResultSummary,
    CapabilityLimitation,
    Error
}

public static class AssistantOutputSanitizer
{
    private static readonly string[] ForbiddenDiagnosticMarkers =
    [
        "ordered tasks",
        "request.atomize",
        "language.prepare",
        "artifact.define",
        "behavior.define",
        "contract.design",
        "implementation.write",
        "validation.scan",
        "answer.revise",
        "result.merge",
        "missing details",
        "revision:",
        "internal critique",
        "cognitive frame",
        "stop reason:",
        "executed observations:",
        "intent:",
        "topic:",
        "confidence:"
    ];

    public static AssistantResponse Sanitize(AssistantResponse response)
    {
        if (!ContainsForbiddenDiagnostic(response.Content))
        {
            return response;
        }

        var fallback = ContainsArabic(response.Content)
            ? "\u062d\u0635\u0644 \u062e\u0644\u0644 \u0641\u064a \u0635\u064a\u0627\u063a\u0629 \u0627\u0644\u0631\u062f. \u0627\u0628\u0639\u062a \u0627\u0644\u0637\u0644\u0628 \u0628\u062a\u0641\u0627\u0635\u064a\u0644 \u0623\u0648\u0636\u062d \u0648\u0647\u0631\u062f \u0639\u0644\u064a\u0647 \u0645\u0628\u0627\u0634\u0631\u0629."
            : "I hit a response formatting problem. Send the request again with the concrete goal and I will answer directly.";
        return new AssistantResponse(AssistantResponseKind.Error, fallback);
    }

    public static bool ContainsForbiddenDiagnostic(string content) =>
        ForbiddenDiagnosticMarkers.Any(marker => content.Contains(marker, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> ForbiddenMarkers => ForbiddenDiagnosticMarkers;

    private static bool ContainsArabic(string text) => text.Any(c => c >= 0x0600 && c <= 0x06FF);
}
