namespace Thoth.Data.Acquisition;

public sealed record AcquisitionSourceDefinition(
    string SourceId,
    string DisplayName,
    string SourceType,
    string OfficialUrl,
    string LicenseSpdx,
    string LicenseUrl,
    bool AttributionRequired,
    long? PilotCompressedByteCap,
    long? PilotExtractedByteCap,
    IReadOnlyList<string> RequiredApprovalFacts,
    IReadOnlyList<AcquisitionPlanStep> Steps);

public sealed record AcquisitionPlanStep(
    int Order,
    string Name,
    string Description,
    bool RequiresNetwork,
    bool RequiresExplicitApproval);

public sealed record DownloadApprovalReport(
    string SourceName,
    string OfficialUrl,
    string LicenseSpdx,
    long? RemoteCompressedBytes,
    long? ExpectedExtractedBytes,
    long FreeDiskBytes,
    bool RequiresExplicitConfirmation,
    IReadOnlyList<string> Reasons);

public sealed class DownloadApprovalPolicy(
    long singleDownloadConfirmationBytes = 2L * 1024 * 1024 * 1024,
    long totalDownloadConfirmationBytes = 5L * 1024 * 1024 * 1024)
{
    public DownloadApprovalReport Evaluate(
        AcquisitionSourceDefinition source,
        long? remoteCompressedBytes,
        long? expectedExtractedBytes,
        long freeDiskBytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        var reasons = new List<string>();
        if (remoteCompressedBytes is null)
        {
            reasons.Add("remote compressed size is unknown");
        }
        else if (remoteCompressedBytes > singleDownloadConfirmationBytes)
        {
            reasons.Add($"single download exceeds {singleDownloadConfirmationBytes:n0} bytes");
        }

        if (expectedExtractedBytes is not null &&
            expectedExtractedBytes > totalDownloadConfirmationBytes)
        {
            reasons.Add($"planned extracted data exceeds {totalDownloadConfirmationBytes:n0} bytes");
        }

        if (expectedExtractedBytes is not null && expectedExtractedBytes > freeDiskBytes)
        {
            reasons.Add("expected extracted data exceeds available free disk space");
        }

        return new DownloadApprovalReport(
            source.DisplayName,
            source.OfficialUrl,
            source.LicenseSpdx,
            remoteCompressedBytes,
            expectedExtractedBytes,
            freeDiskBytes,
            reasons.Count > 0,
            reasons);
    }
}
