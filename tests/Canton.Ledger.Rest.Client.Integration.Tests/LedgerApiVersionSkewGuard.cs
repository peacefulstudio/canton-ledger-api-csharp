// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Integration.Tests;

public static partial class LedgerApiVersionSkewGuard
{
    internal const string VendoredSpecCantonVersion = "3.4.11";

    public enum Verdict
    {
        SameMinor,
        MinorSkew,
        Unparseable,
    }

    [GeneratedRegex(@"^(\d+)\.(\d+)")]
    private static partial Regex MajorMinorPrefix();

    internal static async Task AssertConformableOrSkipAsync(
        IVersionServiceApi versionApi,
        CancellationToken cancellationToken)
    {
        var response = await versionApi.GetLedgerApiVersion(cancellationToken);
        var reportedVersion = response.Version;

        switch (Classify(reportedVersion))
        {
            case Verdict.SameMinor:
                return;
            case Verdict.MinorSkew:
                Assert.Skip(
                    $"Skipping: the LocalNet participant reports Ledger API version '{reportedVersion}' but the "
                    + $"vendored REST spec targets Canton {VendoredSpecCantonVersion}, and conformance results "
                    + "across Canton minors are not trustworthy. Re-run against a Canton "
                    + $"{MajorMinorOf(VendoredSpecCantonVersion)}.x LocalNet, or regenerate the spec for the new "
                    + "minor per src/Canton.Ledger.Rest/spec/provenance.md — version-skew tracking: #176.");
                return;
            default:
                Assert.Fail(
                    $"GET /v2/version returned version '{reportedVersion}', which has no major.minor prefix to "
                    + $"compare against the vendored spec's Canton {VendoredSpecCantonVersion}. A participant "
                    + "that cannot report a comparable version is a defect, not a version skew (#176).");
                return;
        }
    }

    internal static Verdict Classify(string? reportedVersion)
    {
        if (string.IsNullOrWhiteSpace(reportedVersion))
        {
            return Verdict.Unparseable;
        }

        var reportedMinor = MajorMinorPrefix().Match(reportedVersion);
        if (!reportedMinor.Success)
        {
            return Verdict.Unparseable;
        }

        return reportedMinor.Value == MajorMinorOf(VendoredSpecCantonVersion)
            ? Verdict.SameMinor
            : Verdict.MinorSkew;
    }

    private static string MajorMinorOf(string version) => MajorMinorPrefix().Match(version).Value;
}
