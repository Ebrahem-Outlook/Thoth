namespace Thoth.Model;

public sealed record TorchTransformerConfig(
    int VocabularySize,
    int ContextLength,
    int LayerCount,
    int Width,
    int HeadCount,
    int FeedForwardSize,
    double Dropout = 0,
    int Seed = 1337,
    int PaddingToken = -100,
    string Device = "cpu",
    bool TieOutputEmbeddings = false)
{
    public static TorchTransformerConfig Tiny(int vocabularySize, int seed = 1337) =>
        new(vocabularySize, 128, 2, 128, 4, 384, 0, seed);

    public void Validate()
    {
        if (VocabularySize < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(VocabularySize));
        }

        if (ContextLength < 2 || LayerCount < 1 || Width < 8 || HeadCount < 1 || FeedForwardSize < Width)
        {
            throw new ArgumentOutOfRangeException(nameof(ContextLength), "Transformer dimensions are invalid.");
        }

        if (Width % HeadCount != 0)
        {
            throw new ArgumentException("Width must be divisible by head count.", nameof(Width));
        }

        if ((Width / HeadCount) % 2 != 0)
        {
            throw new ArgumentException("Head dimension must be even for RoPE.", nameof(Width));
        }

        if (Dropout is < 0 or >= 1 || double.IsNaN(Dropout))
        {
            throw new ArgumentOutOfRangeException(nameof(Dropout));
        }
    }
}
