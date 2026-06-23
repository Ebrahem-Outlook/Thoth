using Thoth.Core.Understanding;

namespace Thoth.Tests.Understanding;

public sealed class HeuristicUnderstandingServiceTests
{
    [Fact]
    public async Task UnderstandAsync_DetectsWorkspaceTask()
    {
        var service = new HeuristicUnderstandingService();

        var result = await service.UnderstandAsync(new UnderstandingRequest(
            "ابني Angular frontend و backend API للمشروع",
            []));

        Assert.Equal("workspace_task", result.Intent);
        Assert.True(result.RequiresTools);
        Assert.Equal("ar", result.Language);
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
