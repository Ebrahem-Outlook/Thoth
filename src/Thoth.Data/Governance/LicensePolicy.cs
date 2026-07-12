namespace Thoth.Data.Governance;

public enum LicenseDecisionStatus
{
    Allowed,
    ReviewRequired,
    Rejected
}

public sealed record LicenseDecision(
    LicenseDecisionStatus Status,
    string LicenseSpdx,
    string Reason);

public sealed class LicensePolicy
{
    public static LicensePolicy ConservativeDefault { get; } = new(
        allowedLicenses:
        [
            "MIT",
            "Apache-2.0",
            "BSD-2-Clause",
            "BSD-3-Clause",
            "ISC",
            "CC0-1.0",
            "Unlicense"
        ],
        reviewLicenses:
        [
            "CC-BY-SA-2.5",
            "CC-BY-SA-3.0",
            "CC-BY-SA-4.0"
        ],
        rejectedLicenses:
        [
            "NOASSERTION",
            "GPL-2.0",
            "GPL-3.0",
            "AGPL-3.0",
            "LGPL-2.1",
            "LGPL-3.0"
        ]);

    public LicensePolicy(
        IReadOnlyCollection<string> allowedLicenses,
        IReadOnlyCollection<string> reviewLicenses,
        IReadOnlyCollection<string> rejectedLicenses)
    {
        AllowedLicenses = new HashSet<string>(allowedLicenses, StringComparer.OrdinalIgnoreCase);
        ReviewLicenses = new HashSet<string>(reviewLicenses, StringComparer.OrdinalIgnoreCase);
        RejectedLicenses = new HashSet<string>(rejectedLicenses, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlySet<string> AllowedLicenses { get; }

    public IReadOnlySet<string> ReviewLicenses { get; }

    public IReadOnlySet<string> RejectedLicenses { get; }

    public LicenseDecision Evaluate(string? licenseSpdx)
    {
        if (string.IsNullOrWhiteSpace(licenseSpdx))
        {
            return new LicenseDecision(
                LicenseDecisionStatus.Rejected,
                "NOASSERTION",
                "Missing license metadata is rejected by default.");
        }

        var normalized = licenseSpdx.Trim();
        if (AllowedLicenses.Contains(normalized))
        {
            return new LicenseDecision(
                LicenseDecisionStatus.Allowed,
                normalized,
                "License is in the conservative permissive allowlist.");
        }

        if (ReviewLicenses.Contains(normalized))
        {
            return new LicenseDecision(
                LicenseDecisionStatus.ReviewRequired,
                normalized,
                "Share-alike or attribution-sensitive content requires separate labeled handling and attribution.");
        }

        if (RejectedLicenses.Contains(normalized) ||
            normalized.Contains("GPL", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("proprietary", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("custom", StringComparison.OrdinalIgnoreCase))
        {
            return new LicenseDecision(
                LicenseDecisionStatus.Rejected,
                normalized,
                "License is rejected for the first local training experiment.");
        }

        return new LicenseDecision(
            LicenseDecisionStatus.Rejected,
            normalized,
            "Unrecognized licenses are rejected until reviewed explicitly.");
    }
}
