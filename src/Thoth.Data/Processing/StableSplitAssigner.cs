using System.Security.Cryptography;
using System.Text;

namespace Thoth.Data.Processing;

public sealed record SplitRatios(int Train = 96, int Validation = 2, int Test = 2)
{
    public void Validate()
    {
        if (Train < 0 || Validation < 0 || Test < 0 || Train + Validation + Test != 100)
        {
            throw new ArgumentOutOfRangeException(nameof(Train), "Split ratios must be non-negative and sum to 100.");
        }
    }
}

public sealed class StableSplitAssigner(SplitRatios? ratios = null, int seed = 1337)
{
    private readonly SplitRatios ratios = ratios ?? new SplitRatios();

    public string Assign(string groupKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupKey);
        ratios.Validate();
        var bucket = Bucket(groupKey, seed);
        if (bucket < ratios.Train)
        {
            return "train";
        }

        if (bucket < ratios.Train + ratios.Validation)
        {
            return "validation";
        }

        return "test";
    }

    private static int Bucket(string groupKey, int seed)
    {
        var bytes = Encoding.UTF8.GetBytes($"{seed}:{groupKey}");
        var hash = SHA256.HashData(bytes);
        var value = BitConverter.ToUInt32(hash, 0);
        return (int)(value % 100);
    }
}
