namespace Thoth.Model;

public sealed record TorchTransformerProfile(
    string Name,
    TorchTransformerConfig Config,
    long ParameterCount);

public static class TorchTransformerProfiles
{
    public static TorchTransformerProfile SmokeCpu(int vocabularySize = 2_048, int seed = 1337)
    {
        var config = new TorchTransformerConfig(
            VocabularySize: vocabularySize,
            ContextLength: 128,
            LayerCount: 2,
            Width: 128,
            HeadCount: 4,
            FeedForwardSize: 512,
            Dropout: 0,
            Seed: seed,
            Device: "cpu",
            TieOutputEmbeddings: true);
        return new TorchTransformerProfile("smoke-cpu", config, CountParameters(config));
    }

    public static TorchTransformerProfile LaptopPilot(int vocabularySize = 8_000, int seed = 1337)
    {
        var config = new TorchTransformerConfig(
            VocabularySize: vocabularySize,
            ContextLength: 256,
            LayerCount: 4,
            Width: 256,
            HeadCount: 8,
            FeedForwardSize: 1024,
            Dropout: 0,
            Seed: seed,
            Device: "cpu",
            TieOutputEmbeddings: true);
        return new TorchTransformerProfile("laptop-pilot", config, CountParameters(config));
    }

    public static long CountParameters(TorchTransformerConfig config)
    {
        config.Validate();
        var embeddings = (long)config.VocabularySize * config.Width;
        var outputHead = config.TieOutputEmbeddings ? 0 : (long)config.VocabularySize * config.Width;
        var finalNorm = config.Width;
        var perLayer =
            2L * config.Width +
            4L * config.Width * config.Width +
            3L * config.Width * config.FeedForwardSize;
        return embeddings + outputHead + finalNorm + perLayer * config.LayerCount;
    }
}
