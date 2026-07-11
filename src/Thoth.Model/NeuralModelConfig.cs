namespace Thoth.Model;

public sealed record NeuralModelConfig(
    int VocabularySize,
    int EmbeddingSize = 64,
    int HiddenSize = 128,
    int SequenceLength = 128,
    int Seed = 1337)
{
    public void Validate()
    {
        if (VocabularySize < 8)
        {
            throw new ArgumentOutOfRangeException(nameof(VocabularySize), "Vocabulary size must be at least 8.");
        }

        if (EmbeddingSize < 4)
        {
            throw new ArgumentOutOfRangeException(nameof(EmbeddingSize), "Embedding size must be at least 4.");
        }

        if (HiddenSize < 4)
        {
            throw new ArgumentOutOfRangeException(nameof(HiddenSize), "Hidden size must be at least 4.");
        }

        if (SequenceLength < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(SequenceLength), "Sequence length must be at least 2.");
        }
    }
}
