using Thoth.Tokenization;

namespace Thoth.Tests.Neural;

public sealed class ByteTokenizerTests
{
    [Fact]
    public void EncodeDecode_RoundTripsArabicAndEnglish()
    {
        var tokenizer = new ByteTokenizer();
        const string text = "Thoth \u0628\u064a\u0641\u0647\u0645 \u0643\u0644 UTF-8: 123";

        var tokens = tokenizer.Encode(text, addBeginningOfSequence: true, addEndOfSequence: true);
        var decoded = tokenizer.Decode(tokens);

        Assert.Equal(text, decoded);
        Assert.Equal(260, tokenizer.VocabularySize);
    }
}
