using Thoth.Core.Understanding;

namespace Thoth.Tests.Understanding;

public sealed class HeuristicUnderstandingServiceTests
{
    [Fact]
    public async Task UnderstandAsync_DetectsWorkspaceTask()
    {
        var service = new HeuristicUnderstandingService();

        var result = await service.UnderstandAsync(new UnderstandingRequest(
            "\u0627\u0628\u0646\u064a Angular frontend \u0648 backend API \u0644\u0644\u0645\u0634\u0631\u0648\u0639",
            []));

        Assert.Equal("workspace_task", result.Intent);
        Assert.True(result.RequiresTools);
        Assert.Equal("ar", result.Language);
    }

    [Fact]
    public async Task UnderstandAsync_DetectsArabicUiTask()
    {
        var service = new HeuristicUnderstandingService();

        var result = await service.UnderstandAsync(new UnderstandingRequest(
            "\u062d\u0633\u0646 \u0627\u0644\u0648\u0627\u062c\u0647\u0629 \u0648\u0627\u0644\u0627\u0646\u062c\u0644\u0648\u0631 \u0648\u062e\u0644\u064a\u0647\u0627 \u0645\u0645\u062a\u0627\u0632\u0629",
            []));

        Assert.Equal("workspace_task", result.Intent);
        Assert.True(result.RequiresTools);
        Assert.Equal("frontend", result.Topic);
    }

    [Fact]
    public async Task UnderstandAsync_DetectsVisionChat()
    {
        var service = new HeuristicUnderstandingService();

        var result = await service.UnderstandAsync(new UnderstandingRequest(
            "what is in this picture?",
            ["image/png"]));

        Assert.Equal("vision_chat", result.Intent);
        Assert.True(result.RequiresVision);
    }

    [Fact]
    public async Task UnderstandAsync_DetectsModelReasoningTask()
    {
        var service = new HeuristicUnderstandingService();

        var result = await service.UnderstandAsync(new UnderstandingRequest(
            "\u0627\u0644 backend \u0645\u0628\u064a\u0641\u0643\u0631\u0634 \u0648\u0639\u0627\u064a\u0632 \u0645\u0648\u062f\u064a\u0644 \u0630\u0643\u064a \u064a\u062a\u062f\u0631\u0628 \u0645\u062d\u0644\u064a\u0627",
            []));

        Assert.Equal("workspace_task", result.Intent);
        Assert.Equal("model", result.Topic);
        Assert.True(result.RequiresTools);
    }

    [Theory]
    [InlineData("Hello")]
    [InlineData("\u0627\u0646\u062a \u0641\u0627\u0647\u0645\u0646\u064a")]
    public async Task UnderstandAsync_KeepsCasualChatGeneral(string text)
    {
        var service = new HeuristicUnderstandingService();

        var result = await service.UnderstandAsync(new UnderstandingRequest(text, []));

        Assert.Equal("general_chat", result.Intent);
        Assert.Equal("general", result.Topic);
        Assert.False(result.RequiresTools);
    }

    [Theory]
    [InlineData("can you build a C# method")]
    [InlineData("\u0639\u0627\u064a\u0632\u0643 \u062a\u0639\u0645\u0644\u064a meethod in C#")]
    [InlineData("\u0641\u064a\u0646 \u0627\u0644 method \u062a\u0639\u0631\u0641 \u062a\u0639\u0645\u0644\u064a")]
    public async Task UnderstandAsync_RoutesGenericMethodRequestsToCodeGeneration(string text)
    {
        var service = new HeuristicUnderstandingService();

        var result = await service.UnderstandAsync(new UnderstandingRequest(text, []));

        Assert.Equal("code_generation", result.Intent);
        Assert.Equal("coding", result.Topic);
        Assert.False(result.RequiresTools);
    }

    [Theory]
    [InlineData("add a C# method to Program.cs")]
    [InlineData("\u0636\u064a\u0641 method \u0641\u064a \u0645\u0644\u0641 Program.cs")]
    public async Task UnderstandAsync_RoutesProjectBoundMethodRequestsToBackendTools(string text)
    {
        var service = new HeuristicUnderstandingService();

        var result = await service.UnderstandAsync(new UnderstandingRequest(text, []));

        Assert.Equal("workspace_task", result.Intent);
        Assert.Equal("backend", result.Topic);
        Assert.True(result.RequiresTools);
    }
}
