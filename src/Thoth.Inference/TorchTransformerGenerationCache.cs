namespace Thoth.Inference;

public sealed class TorchTransformerGenerationCache : IDisposable
{
    private readonly List<int> tokens = [];
    private bool disposed;

    public IReadOnlyList<int> Tokens => tokens;

    public void Reset(IEnumerable<int> promptTokens)
    {
        ThrowIfDisposed();
        tokens.Clear();
        tokens.AddRange(promptTokens);
    }

    public void Append(int tokenId)
    {
        ThrowIfDisposed();
        tokens.Add(tokenId);
    }

    public void Dispose()
    {
        tokens.Clear();
        disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(TorchTransformerGenerationCache));
        }
    }
}
