using System.Text;

namespace Thoth.Tokenization;

/// <summary>
/// A deterministic UTF-8 byte tokenizer. It has no learned vocabulary and can
/// represent every language without unknown tokens. It is intentionally used
/// as Thoth's bootstrap tokenizer; a learned BPE tokenizer can replace it later
/// without changing the model or training contracts.
/// </summary>
public sealed class ByteTokenizer : ITextTokenizer
{
    public const int SpecialTokenCount = 4;

    public int VocabularySize => SpecialTokenCount + 256;

    public int PaddingTokenId => 0;

    public int BeginningOfSequenceTokenId => 1;

    public int EndOfSequenceTokenId => 2;

    public int SeparatorTokenId => 3;

    public IReadOnlyList<int> Encode(
        string text,
        bool addBeginningOfSequence = false,
        bool addEndOfSequence = false)
    {
        ArgumentNullException.ThrowIfNull(text);

        var bytes = Encoding.UTF8.GetBytes(text);
        var tokenCount = bytes.Length + (addBeginningOfSequence ? 1 : 0) + (addEndOfSequence ? 1 : 0);
        var tokens = new int[tokenCount];
        var index = 0;

        if (addBeginningOfSequence)
        {
            tokens[index++] = BeginningOfSequenceTokenId;
        }

        foreach (var value in bytes)
        {
            tokens[index++] = SpecialTokenCount + value;
        }

        if (addEndOfSequence)
        {
            tokens[index] = EndOfSequenceTokenId;
        }

        return tokens;
    }

    public string Decode(IEnumerable<int> tokenIds, bool skipSpecialTokens = true)
    {
        ArgumentNullException.ThrowIfNull(tokenIds);

        var bytes = new List<byte>();
        foreach (var tokenId in tokenIds)
        {
            if (tokenId >= SpecialTokenCount && tokenId < VocabularySize)
            {
                bytes.Add((byte)(tokenId - SpecialTokenCount));
                continue;
            }

            if (!skipSpecialTokens && tokenId == SeparatorTokenId)
            {
                bytes.Add((byte)'\n');
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
