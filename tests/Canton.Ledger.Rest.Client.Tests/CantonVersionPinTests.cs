// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;
using static Canton.Ledger.Rest.Client.Integration.Tests.LedgerApiVersionSkewGuard;

namespace Canton.Ledger.Rest.Client.Tests;

public class CantonVersionPinTests
{
    private const string VersionKey = "version:";

    private static string VendoredSpecPath => Path.Combine(AppContext.BaseDirectory, "spec", "openapi.yaml");

    [Fact]
    public void CantonVersionPin_reaches_the_skew_guard_as_a_non_empty_build_property()
        => VendoredSpecCantonVersion.Should().NotBeNullOrWhiteSpace();

    [Fact]
    public void CantonVersionPin_sits_on_a_canton_minor_the_skew_guard_accepts()
        => AcceptedCantonMinors.Should().Contain(MajorMinorOf(VendoredSpecCantonVersion));

    [Fact]
    public void VendoredSpec_declares_the_canton_version_the_build_pins()
        => VendoredSpecInfoVersion().Should().Be(
            VendoredSpecCantonVersion,
            "spec/openapi.yaml info.version is derived from $(CantonVersion) at regeneration time; re-run "
            + "scripts/regen-rest-client.sh and commit the result rather than editing either side by hand");

    private static string VendoredSpecInfoVersion()
    {
        var lines = File.ReadAllLines(VendoredSpecPath);
        var infoIndex = Array.FindIndex(lines, line => line.StartsWith("info:", StringComparison.Ordinal));
        if (infoIndex < 0)
        {
            throw new InvalidOperationException($"{VendoredSpecPath} declares no top-level info block.");
        }

        var versionLine = lines.Skip(infoIndex + 1)
            .TakeWhile(line => line.StartsWith(' '))
            .Select(line => line.Trim())
            .SingleOrDefault(line => line.StartsWith(VersionKey, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"{VendoredSpecPath} declares no info.version.");

        return versionLine[VersionKey.Length..].Trim();
    }
}
