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
}
