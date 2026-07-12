namespace Thoth.Training.TokenShards;

public sealed record TokenWindowReference(
    int DocumentIndex,
    int DocumentOffset);

public sealed class TokenWindowDataset(
    TokenShardReader reader,
    int contextLength,
    int stride,
    int seed = 1337)
{
    public IReadOnlyList<TokenWindowReference> CreateEpochOrder(int epoch)
    {
        if (contextLength < 1 || stride < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(contextLength));
        }

        var references = new List<TokenWindowReference>();
        for (var docIndex = 0; docIndex < reader.Header.Documents.Count; docIndex++)
        {
            var document = reader.Header.Documents[docIndex];
            if (document.TokenCount < 2)
            {
                continue;
            }

            for (var offset = 0; offset < document.TokenCount - 1; offset += stride)
            {
                references.Add(new TokenWindowReference(docIndex, offset));
            }
        }

        var random = new Random(seed + epoch);
        for (var index = references.Count - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (references[index], references[swap]) = (references[swap], references[index]);
        }

        return references;
    }

    public IEnumerable<TokenWindow> ReadEpoch(int epoch)
    {
        foreach (var reference in CreateEpochOrder(epoch))
        {
            var document = reader.Header.Documents[reference.DocumentIndex];
            yield return reader.ReadWindow(document, reference.DocumentOffset, contextLength);
        }
    }
}
