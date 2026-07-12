namespace Thoth.Model;

public sealed record TransformerConfig(
    int VocabularySize,
    int ContextLength = 128,
    int LayerCount = 2,
    int Width = 128,
    int HeadCount = 4,
    int FeedForwardSize = 256,
    double Dropout = 0,
    int Seed = 1337,
    bool TieOutputEmbeddings = false)
{
    public static TransformerConfig Tiny(int vocabularySize, int seed = 1337) =>
        new(vocabularySize, ContextLength: 128, LayerCount: 2, Width: 128, HeadCount: 4, FeedForwardSize: 256, Dropout: 0, Seed: seed);

    public static TransformerConfig Bootstrap(int vocabularySize, int seed = 1337) =>
        new(vocabularySize, ContextLength: 1024, LayerCount: 8, Width: 512, HeadCount: 8, FeedForwardSize: 2048, Dropout: 0.1, Seed: seed);

    public void Validate()
    {
        if (VocabularySize < 8)
        {
            throw new ArgumentOutOfRangeException(nameof(VocabularySize), "Vocabulary size must be at least 8.");
        }

        if (ContextLength < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(ContextLength), "Context length must be at least 2.");
        }

        if (LayerCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(LayerCount), "Layer count must be positive.");
        }

        if (Width < 8 || Width % HeadCount != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Width), "Width must be at least 8 and divisible by the head count.");
        }

        if (HeadCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(HeadCount), "Head count must be positive.");
        }

        if ((Width / HeadCount) % 2 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(HeadCount), "RoPE requires an even head dimension.");
        }

        if (FeedForwardSize < Width)
        {
            throw new ArgumentOutOfRangeException(nameof(FeedForwardSize), "Feed-forward size must be at least the model width.");
        }

        if (Dropout is < 0 or >= 1 || double.IsNaN(Dropout))
        {
            throw new ArgumentOutOfRangeException(nameof(Dropout), "Dropout must be in [0, 1).");
        }
    }
}

