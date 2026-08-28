// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text.RegularExpressions;

namespace Canton.Ledger.Rest.Client.Integration.Tests;

public static partial class LedgerApiVersionSkewGuard
{
    private const string CantonVersionMetadataKey = "CantonVersion";

    internal static string VendoredSpecCantonVersion => ReadCantonVersionPin();

    internal static IReadOnlySet<string> AcceptedCantonMinors { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "3.5" };

    private static string ReadCantonVersionPin()
    {
        var pin = typeof(LedgerApiVersionSkewGuard).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(metadata => metadata.Key == CantonVersionMetadataKey)
            ?.Value;

        return string.IsNullOrWhiteSpace(pin) ? throw new InvalidOperationException(MissingPinMessage()) : pin;
    }

    private static string MissingPinMessage() =>
        $"Assembly '{typeof(LedgerApiVersionSkewGuard).Assembly.GetName().Name}' compiles "
        + $"{nameof(LedgerApiVersionSkewGuard)} but carries no '{CantonVersionMetadataKey}' AssemblyMetadata, so "
        + "the vendored-spec Canton version cannot be resolved. That pin is authored once as $(CantonVersion) in "
        + "Directory.Build.props and surfaced to every test assembly by tests/Directory.Build.props; restore that "
        + "item rather than re-declaring the version here.";

    public enum Verdict
    {
        Supported,
        Unsupported,
        Unparseable,
    }

    [GeneratedRegex(@"^(\d+)\.(\d+)")]
    private static partial Regex MajorMinorPrefix();

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

        return AcceptedCantonMinors.Contains(reportedMinor.Value)
            ? Verdict.Supported
            : Verdict.Unsupported;
    }

    internal static string MajorMinorOf(string version) => MajorMinorPrefix().Match(version).Value;

    internal static string UnsupportedVersionFailureMessage(string? reportedVersion) =>
        $"REST conformance FAILED (not skipped): the LocalNet participant reports Ledger API version "
        + $"'{reportedVersion}', whose Canton minor is outside the accepted set "
        + $"({AcceptedMinorList()}). The vendored REST spec targets Canton {VendoredSpecCantonVersion}, and "
        + "conformance results across Canton minors are not trustworthy, so an unaccepted minor is a hard "
        + "failure with no environment override. Re-run against a LocalNet on an accepted minor, or re-vendor "
        + "the spec for the new minor per src/Canton.Ledger.Rest/README.md and widen the accepted set.";

    internal static string UnparseableVersionFailureMessage(string? reportedVersion) =>
        $"GET /v2/version returned version '{reportedVersion}', which has no major.minor prefix to compare "
        + $"against the accepted set ({AcceptedMinorList()}). A participant that cannot report a comparable "
        + "version is a defect, not a version skew.";

    private static string AcceptedMinorList() =>
        string.Join(", ", AcceptedCantonMinors.Order(StringComparer.Ordinal).Select(minor => $"{minor}.x"));
}
