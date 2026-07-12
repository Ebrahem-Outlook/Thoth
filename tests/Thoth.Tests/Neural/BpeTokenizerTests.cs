using Thoth.Tokenization;

namespace Thoth.Tests.Neural;

public sealed class BpeTokenizerTests
{
    private static readonly string[] Corpus =
    [
        "\u0645\u0631\u062d\u0628\u0627 \u064a\u0627 \u0645\u0639\u0644\u0645\u060c \u062b\u0648\u062b \u0628\u064a\u0641\u0647\u0645 \u0639\u0631\u0628\u064a \u0648English.",
        "\u0627\u0632\u064a\u0643 \u064a\u0627 \u0635\u0627\u062d\u0628\u064a\u061f \u0639\u0627\u064a\u0632\u0643 \u062a\u0638\u0628\u0637 \u0627\u0644\u0645\u0648\u062f\u064a\u0644 \u062d\u0627\u0644\u0627.",
        "English punctuation: hello, world! Does byte-level BPE round-trip everything?",
        "#include <vector>\ntemplate <typename T>\nstd::vector<T> append(std::vector<T> xs, T value) { xs.push_back(value); return xs; }",
        "@Component({ selector: 'thoth-panel' })\nexport class Box<T extends Record<string, unknown>> { value!: T; }",
        "public static IEnumerable<string?> Clean(IEnumerable<string?> values) => values.Where(v => v is not null).Select(v => v!.Trim());",
        "emoji and punctuation: \U0001F680 \U0001F9E0 !!! ??? -- ++ == != <= >="
    ];

    [Theory]
    [MemberData(nameof(RoundTripTexts))]
    public void EncodeDecode_RoundTripsLanguagesCodeEmojiAndPunctuation(string text)
    {
        var tokenizer = TrainTokenizer();

        var decoded = tokenizer.Decode(tokenizer.Encode(text, addBeginningOfSequence: true, addEndOfSequence: true));

        Assert.Equal(text, decoded);
    }

    [Fact]
    public void Train_IsDeterministicForFixedCorpusAndOptions()
    {
        var options = new BpeTokenizerTrainingOptions(360, MinimumPairFrequency: 2, Profile: "test");

        var left = BpeTokenizer.Train(Corpus, options);
        var right = BpeTokenizer.Train(Corpus, options);

        Assert.Equal(left.VocabularySize, right.VocabularySize);
        Assert.Equal(left.TrainingManifestSha256, right.TrainingManifestSha256);
        Assert.Equal(left.Merges, right.Merges);
        Assert.Equal(left.Encode(Corpus[3]), right.Encode(Corpus[3]));
    }

    [Fact]
    public async Task SaveLoad_WritesCompletePackageAndPreservesEncoding()
    {
        var tokenizer = TrainTokenizer();
        var path = TempDirectory();

        await tokenizer.SaveAsync(path);
        var loaded = await BpeTokenizer.LoadAsync(path);

        Assert.True(File.Exists(Path.Combine(path, "tokenizer.json")));
        Assert.True(File.Exists(Path.Combine(path, "vocab.json")));
        Assert.True(File.Exists(Path.Combine(path, "merges.txt")));
        Assert.True(File.Exists(Path.Combine(path, "tokenizer-metadata.json")));
        Assert.True(File.Exists(Path.Combine(path, "training-manifest.sha256")));
        Assert.Equal(tokenizer.VocabularySize, loaded.VocabularySize);
        Assert.Equal(tokenizer.Merges, loaded.Merges);
        Assert.Equal(Corpus[4], loaded.Decode(loaded.Encode(Corpus[4])));
    }

    [Fact]
    public async Task Load_AcceptsLegacySingleFileArtifact()
    {
        var path = TempDirectory();
        var artifact = """
            {
              "version": 1,
              "tokenizer": "bpe-v1",
              "vocabularySize": 261,
              "merges": [
                { "left": 97, "right": 98, "symbol": 256 }
              ]
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(path, "tokenizer.json"), artifact);

        var tokenizer = await BpeTokenizer.LoadAsync(path);

        Assert.Equal(4 + 256 + 1, tokenizer.VocabularySize);
        Assert.Equal(3, tokenizer.SeparatorTokenId);
        Assert.Equal("ab", tokenizer.Decode(tokenizer.Encode("ab")));
    }

    [Fact]
    public async Task Load_RejectsCorruptedArtifactHash()
    {
        var tokenizer = TrainTokenizer();
        var path = TempDirectory();
        await tokenizer.SaveAsync(path);

        await File.AppendAllTextAsync(Path.Combine(path, "merges.txt"), "0 0 999\n");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => BpeTokenizer.LoadAsync(path));
        Assert.Contains("hash mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TrainFromFiles_UsesDeterministicFileOrderAndManifestHash()
    {
        var path = TempDirectory();
        await File.WriteAllTextAsync(Path.Combine(path, "b.txt"), Corpus[1]);
        await File.WriteAllTextAsync(Path.Combine(path, "a.txt"), Corpus[0]);
        var options = new BpeTokenizerTrainingOptions(320, MinimumPairFrequency: 2, Profile: "files-test");

        var first = await BpeTokenizer.TrainFromFilesAsync(path, options);
        var second = await BpeTokenizer.TrainFromFilesAsync(path, options);

        Assert.Equal(first.TrainingManifestSha256, second.TrainingManifestSha256);
        Assert.Equal(first.Merges, second.Merges);
    }

    [Fact]
    public void EncodeDecode_PreservesSpecialTokenBoundaries()
    {
        var tokenizer = TrainTokenizer();
        const string chat = "<USER>Hello<END_TURN><ASSISTANT>Hi<END_TURN>";

        var tokens = tokenizer.Encode(chat);

        Assert.Contains(tokenizer.GetSpecialTokenId("<USER>"), tokens);
        Assert.Contains(tokenizer.GetSpecialTokenId("<ASSISTANT>"), tokens);
        Assert.Contains(tokenizer.GetSpecialTokenId("<END_TURN>"), tokens);
        Assert.Equal(chat, tokenizer.Decode(tokens, skipSpecialTokens: false));
        Assert.Equal("HelloHi", tokenizer.Decode(tokens));
    }

    [Fact]
    public void ByteFallback_RoundTripsTextWithUnseenBytes()
    {
        var tokenizer = BpeTokenizer.Train(["abc abc abc"], new BpeTokenizerTrainingOptions(280));
        const string unseen = "\u0000\u0001\u007F\u0100\U0010FFFF\U0001F916";

        var decoded = tokenizer.Decode(tokenizer.Encode(unseen));

        Assert.Equal(unseen, decoded);
    }

    [Fact]
    public void Profiles_ExposeRequiredLaptopVocabularySizes()
    {
        Assert.Equal(2_048, BpeTokenizerProfiles.Resolve("smoke").VocabularySize);
        Assert.Equal(8_000, BpeTokenizerProfiles.Resolve("laptop-pilot").VocabularySize);
        Assert.Equal(12_000, BpeTokenizerProfiles.Resolve("laptop-max").VocabularySize);
    }

    public static IEnumerable<object[]> RoundTripTexts() =>
        Corpus.Select(text => new object[] { text });

    private static BpeTokenizer TrainTokenizer() =>
        BpeTokenizer.Train(Corpus, new BpeTokenizerTrainingOptions(420, MinimumPairFrequency: 2, Profile: "test"));

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "thoth-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
