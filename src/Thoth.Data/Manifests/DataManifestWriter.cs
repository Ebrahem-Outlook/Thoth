using System.Text;
using System.Text.Json;
using Thoth.Data.Governance;

namespace Thoth.Data.Manifests;

public static class DataManifestWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task EnsureSkeletonAsync(
        string manifestDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestDirectory);
        var directory = Path.GetFullPath(manifestDirectory);
        Directory.CreateDirectory(directory);

        await WriteIfMissingAsync(
            Path.Combine(directory, "sources.json"),
            JsonSerializer.Serialize(new DataSourceManifest(1, []), JsonOptions) + Environment.NewLine,
            cancellationToken);

        await WriteIfMissingAsync(
            Path.Combine(directory, "documents.jsonl"),
            string.Empty,
            cancellationToken);

        await WriteIfMissingAsync(
            Path.Combine(directory, "licenses.json"),
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                policy = "conservative-default",
                allowed = LicensePolicy.ConservativeDefault.AllowedLicenses.Order(StringComparer.OrdinalIgnoreCase),
                reviewRequired = LicensePolicy.ConservativeDefault.ReviewLicenses.Order(StringComparer.OrdinalIgnoreCase),
                rejected = LicensePolicy.ConservativeDefault.RejectedLicenses.Order(StringComparer.OrdinalIgnoreCase)
            }, JsonOptions) + Environment.NewLine,
            cancellationToken);

        await WriteIfMissingAsync(
            Path.Combine(directory, "dataset-build.json"),
            JsonSerializer.Serialize(
                new DatasetBuildManifest(
                    1,
                    "unbuilt-local-dataset",
                    1337,
                    "conservative-default",
                    [
                        "sources.json",
                        "documents.jsonl",
                        "licenses.json",
                        "exclusions.jsonl",
                        "attribution.md"
                    ],
                    new Dictionary<string, string>
                    {
                        ["status"] = "skeleton only; no dataset has been acquired or normalized"
                    }),
                JsonOptions) + Environment.NewLine,
            cancellationToken);

        await WriteIfMissingAsync(
            Path.Combine(directory, "exclusions.jsonl"),
            string.Empty,
            cancellationToken);

        await WriteIfMissingAsync(
            Path.Combine(directory, "attribution.md"),
            "# Dataset Attribution\n\nNo external training sources have been acquired yet.\n",
            cancellationToken);
    }

    private static async Task WriteIfMissingAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            return;
        }

        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, Encoding.UTF8, cancellationToken);
            File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
