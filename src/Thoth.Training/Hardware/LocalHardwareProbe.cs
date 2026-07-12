using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using TorchSharp;
using static TorchSharp.torch;

namespace Thoth.Training.Hardware;

public sealed record LocalHardwareProfile(
    string OperatingSystem,
    string Architecture,
    string? CpuName,
    int LogicalCpuCores,
    int? PhysicalCpuCores,
    int RecommendedTorchCpuThreads,
    long? TotalRamBytes,
    long? AvailableRamBytes,
    IReadOnlyList<DiskSpaceReport> Disks,
    TorchBackendReport Torch,
    IReadOnlyList<WritableDirectoryReport> WritableDirectories);

public sealed record DiskSpaceReport(
    string Name,
    string Root,
    long TotalBytes,
    long FreeBytes);

public sealed record TorchBackendReport(
    bool CpuBackendAvailable,
    bool CudaAvailable,
    string Device,
    IReadOnlyDictionary<string, bool> DtypeChecks,
    string? Error);

public sealed record WritableDirectoryReport(
    string Purpose,
    string Path,
    bool Exists,
    bool Writable,
    string? Error);

public static class LocalHardwareProbe
{
    public static LocalHardwareProfile Inspect(IReadOnlyDictionary<string, string> writableDirectories)
    {
        ArgumentNullException.ThrowIfNull(writableDirectories);
        var memory = TryGetMemoryStatus();
        var logicalCores = Math.Max(1, Environment.ProcessorCount);

        return new LocalHardwareProfile(
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            TryGetCpuName(),
            logicalCores,
            TryGetPhysicalCoreCount(),
            Math.Max(1, logicalCores - 2),
            memory?.TotalPhysicalBytes,
            memory?.AvailablePhysicalBytes,
            GetDiskReports(),
            InspectTorch(),
            writableDirectories
                .Select(item => InspectWritableDirectory(item.Key, item.Value))
                .ToArray());
    }

    private static TorchBackendReport InspectTorch()
    {
        var dtypeChecks = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        string? error = null;
        var cpuOk = false;
        var cudaAvailable = false;

        try
        {
            using var left = ones([2, 2], dtype: ScalarType.Float32, device: CPU);
            using var right = ones([2, 2], dtype: ScalarType.Float32, device: CPU);
            using var multiplied = matmul(left, right);
            cpuOk = multiplied.sum().ToSingle() == 8;

            dtypeChecks["float32_cpu"] = CheckDtype(ScalarType.Float32);
            dtypeChecks["float64_cpu"] = CheckDtype(ScalarType.Float64);
            cudaAvailable = cuda.is_available();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ExternalException or NotSupportedException)
        {
            error = exception.Message;
        }

        return new TorchBackendReport(
            cpuOk,
            cudaAvailable,
            cudaAvailable ? "cuda" : "cpu",
            dtypeChecks,
            error);
    }

    private static bool CheckDtype(ScalarType dtype)
    {
        try
        {
            using var tensor = ones([4], dtype: dtype, device: CPU);
            using var sum = tensor.sum();
            return double.IsFinite(sum.ToDouble());
        }
        catch (Exception exception) when (exception is InvalidOperationException or ExternalException or NotSupportedException)
        {
            return false;
        }
    }

    private static IReadOnlyList<DiskSpaceReport> GetDiskReports()
    {
        return DriveInfo.GetDrives()
            .Where(drive => drive.IsReady && drive.DriveType is DriveType.Fixed or DriveType.Removable)
            .Select(drive => new DiskSpaceReport(
                drive.Name,
                drive.RootDirectory.FullName,
                drive.TotalSize,
                drive.AvailableFreeSpace))
            .OrderBy(report => report.Root, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static WritableDirectoryReport InspectWritableDirectory(string purpose, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var existed = Directory.Exists(fullPath);
        var probePath = Path.Combine(fullPath, $".thoth-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(fullPath);
            File.WriteAllText(probePath, "thoth local hardware probe");
            File.Delete(probePath);
            return new WritableDirectoryReport(purpose, fullPath, existed, true, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new WritableDirectoryReport(purpose, fullPath, existed, false, exception.Message);
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }
    }

    private static MemoryStatus? TryGetMemoryStatus()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            return total > 0 ? new MemoryStatus(total, null) : null;
        }

        var status = new MemoryStatusEx();
        status.Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        return GlobalMemoryStatusEx(ref status)
            ? new MemoryStatus((long)status.TotalPhys, (long)status.AvailPhys)
            : null;
    }

    private static string? TryGetCpuName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                return key?.GetValue("ProcessorNameString")?.ToString()?.Trim();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        if (File.Exists("/proc/cpuinfo"))
        {
            return File.ReadLines("/proc/cpuinfo")
                .FirstOrDefault(line => line.StartsWith("model name", StringComparison.OrdinalIgnoreCase))
                ?.Split(':', 2)
                .LastOrDefault()
                ?.Trim();
        }

        return null;
    }

    private static int? TryGetPhysicalCoreCount()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        var output = TryRun("wmic", "cpu get NumberOfCores /value");
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var cores = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("NumberOfCores=", StringComparison.OrdinalIgnoreCase))
            .Select(line => line.Split('=', 2).LastOrDefault())
            .Select(value => int.TryParse(value, out var parsed) ? parsed : 0)
            .Where(value => value > 0)
            .Sum();

        return cores > 0 ? cores : null;
    }

    private static string? TryRun(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return null;
            }

            if (!process.WaitForExit(milliseconds: 2_000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd() : null;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            return null;
        }
    }

    private sealed record MemoryStatus(long TotalPhysicalBytes, long? AvailablePhysicalBytes);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
