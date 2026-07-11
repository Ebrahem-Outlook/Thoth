namespace Thoth.Tokenization;

public interface ITextTokenizer
{
    int VocabularySize { get; }

    int PaddingTokenId { get; }

    int BeginningOfSequenceTokenId { get; }

    int EndOfSequenceTokenId { get; }

    int SeparatorTokenId { get; }

    IReadOnlyList<int> Encode(string text, bool addBeginningOfSequence = false, bool addEndOfSequence = false);

    string Decode(IEnumerable<int> tokenIds, bool skipSpecialTokens = true);
}
