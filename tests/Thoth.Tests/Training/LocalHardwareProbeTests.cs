using Thoth.Training.Hardware;

namespace Thoth.Tests.Training;

public sealed class LocalHardwareProbeTests
{
    [Fact]
    public void Inspect_ReportsCpuTorchAndWritableDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "thoth-tests", Guid.NewGuid().ToString("N"));
        var profile = LocalHardwareProbe.Inspect(new Dictionary<string, string>
        {
            ["training"] = Path.Combine(root, "training"),
            ["checkpoints"] = Path.Combine(root, "models")
        });

        Assert.False(string.IsNullOrWhiteSpace(profile.OperatingSystem));
        Assert.True(profile.LogicalCpuCores >= 1);
        Assert.True(profile.RecommendedTorchCpuThreads >= 1);
        Assert.True(profile.Torch.CpuBackendAvailable, profile.Torch.Error);
        Assert.Contains(profile.WritableDirectories, directory => directory.Purpose == "training" && directory.Writable);
        Assert.Contains(profile.WritableDirectories, directory => directory.Purpose == "checkpoints" && directory.Writable);
    }
}
