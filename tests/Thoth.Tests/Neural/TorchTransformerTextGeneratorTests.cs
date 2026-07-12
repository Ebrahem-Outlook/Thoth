using Thoth.Inference;
using Thoth.Model;
using Thoth.Tokenization;

namespace Thoth.Tests.Neural;

public sealed class TorchTransformerTextGeneratorTests
{
    [Fact]
    public async Task GenerateTokenIds_GreedyMatchesWithAndWithoutCache()
    {
        var tokenizer = new ByteTokenizer();
        using var model = CreateModel(tokenizer.VocabularySize);
        var generator = new TorchTransformerTextGenerator(model, tokenizer);
        var options = new GenerationOptions { MaxNewTokens = 4, Greedy = true };

        var uncached = await generator.GenerateTokenIdsAsync("abc", options);
        using var cache = new TorchTransformerGenerationCache();
        var cached = await generator.GenerateTokenIdsAsync("abc", options, cache);

        Assert.Equal(uncached, cached);
    }

    [Fact]
    public async Task GenerateTokenIds_StreamsTokensAndHonorsStopToken()
    {
        var tokenizer = new ByteTokenizer();
        using var model = CreateModel(tokenizer.VocabularySize);
        var generator = new TorchTransformerTextGenerator(model, tokenizer);
        var first = (await generator.GenerateTokenIdsAsync("abc", new GenerationOptions { MaxNewTokens = 1, Greedy = true }))[0];
        var streamed = new List<int>();

        var stopped = await generator.GenerateTokenIdsAsync(
            "abc",
            new GenerationOptions { MaxNewTokens = 5, Greedy = true, StopTokenIds = [first] },
            onToken: (token, _, _) =>
            {
                streamed.Add(token);
                return Task.CompletedTask;
            });

        Assert.Empty(stopped);
        Assert.Empty(streamed);

        await generator.GenerateTokenIdsAsync(
            "abc",
            new GenerationOptions { MaxNewTokens = 2, Greedy = true },
            onToken: (token, _, _) =>
            {
                streamed.Add(token);
                return Task.CompletedTask;
            });
        Assert.Equal(2, streamed.Count);
    }

    private static TorchTransformerLanguageModel CreateModel(int vocabularySize) =>
        new(new TorchTransformerConfig(
            VocabularySize: vocabularySize,
            ContextLength: 16,
            LayerCount: 1,
            Width: 16,
            HeadCount: 4,
            FeedForwardSize: 32,
            Dropout: 0,
            Seed: 123));
}
