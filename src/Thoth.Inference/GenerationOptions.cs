namespace Thoth.Inference;

public sealed record GenerationOptions
{
    public int MaxNewTokens { get; init; } = 256;

    public double Temperature { get; init; } = 0.8;

    public int TopK { get; init; } = 40;

    public double TopP { get; init; } = 1.0;

    public int? Seed { get; init; }

    public void Validate()
    {
        if (MaxNewTokens < 1 || MaxNewTokens > 8192)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxNewTokens));
        }

        if (Temperature <= 0 || double.IsNaN(Temperature) || double.IsInfinity(Temperature))
        {
            throw new ArgumentOutOfRangeException(nameof(Temperature));
        }

        if (TopK < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TopK));
        }

        if (TopP <= 0 || TopP > 1 || double.IsNaN(TopP))
        {
            throw new ArgumentOutOfRangeException(nameof(TopP));
        }
    }
}
