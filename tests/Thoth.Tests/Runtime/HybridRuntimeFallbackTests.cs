using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Thoth.Core.Chat;
using Thoth.Model;
using Thoth.Model.Persistence;
using Thoth.Runtime;
using Thoth.Tokenization;

namespace Thoth.Tests.Runtime;

public sealed class HybridRuntimeFallbackTests
{
    [Fact]
    public async Task HybridRuntime_MissingCheckpointReturnsUsefulSelfContainedResponse()
    {
        var root = Path.Combine(Path.GetTempPath(), "thoth-tests", Guid.NewGuid().ToString("N"));
        var checkpointPath = Path.Combine(root, "models", "missing.bin");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Thoth:WorkspaceRoot"] = root,
                ["Thoth:DataDirectory"] = Path.Combine(root, "data"),
                ["Thoth:Model:Provider"] = "hybrid",
                ["Thoth:Model:CheckpointPath"] = checkpointPath
            })
            .Build();

        var services = new ServiceCollection();
        services.AddThothRuntime(configuration);
        await using var provider = services.BuildServiceProvider();
        var model = provider.GetRequiredService<IChatModel>();

        var response = await model.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, "can you build C# method work as calculator")],
            "thoth-self",
            Purpose: ModelRequestPurpose.DirectReply,
            Input: new DirectReplyModelInput("can you build C# method work as calculator")));

        Assert.Contains("```csharp", response.Content);
        Assert.Contains("public static decimal Calculate", response.Content);
        Assert.DoesNotContain("checkpoint file is missing", response.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HybridRuntime_ExperimentalCheckpointFallsBackWithoutLeakingDiagnostics()
    {
        var root = Path.Combine(Path.GetTempPath(), "thoth-tests", Guid.NewGuid().ToString("N"));
        var checkpointPath = Path.Combine(root, "models", "experimental.bin");
        var tokenizer = new ByteTokenizer();
        var model = new RecurrentLanguageModel(new NeuralModelConfig(
            tokenizer.VocabularySize,
            EmbeddingSize: 8,
            HiddenSize: 12,
            SequenceLength: 8,
            Seed: 24));
        var tokens = tokenizer.Encode("quality-check").ToArray();
        model.TrainSequence(tokens[..8], tokens[1..9], 0.005);
        await ModelCheckpoint.SaveAsync(checkpointPath, model);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Thoth:WorkspaceRoot"] = root,
                ["Thoth:DataDirectory"] = Path.Combine(root, "data"),
                ["Thoth:Model:Provider"] = "hybrid",
                ["Thoth:Model:CheckpointPath"] = checkpointPath
            })
            .Build();

        var services = new ServiceCollection();
        services.AddThothRuntime(configuration);
        await using var provider = services.BuildServiceProvider();
        var chatModel = provider.GetRequiredService<IChatModel>();

        var response = await chatModel.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, "can you build C# method work as calculator")],
            "thoth-self",
            Purpose: ModelRequestPurpose.DirectReply,
            Input: new DirectReplyModelInput("can you build C# method work as calculator")));

        Assert.Contains("```csharp", response.Content);
        Assert.DoesNotContain("experimental", response.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mandatory evaluation metrics", response.Content, StringComparison.OrdinalIgnoreCase);
    }
}
