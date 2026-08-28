// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;
using static Canton.Ledger.Rest.Client.Integration.Tests.LedgerApiVersionSkewGuard;

namespace Canton.Ledger.Rest.Client.Tests;

public class LedgerApiVersionSkewGuardTests
{
    [Theory]
    [InlineData("3.5.9")]
    [InlineData("3.5.0")]
    [InlineData("3.5.20-snapshot.20260101.0")]
    public void Classify_accepts_a_participant_on_an_accepted_canton_minor(string reportedVersion)
        => Classify(reportedVersion).Should().Be(Verdict.Supported);

    [Theory]
    [InlineData("3.4.11")]
    [InlineData("3.4.9")]
    [InlineData("3.3.0")]
    [InlineData("3.6.0")]
    [InlineData("3.7.1-snapshot.20260101.0")]
    [InlineData("4.5.9")]
    public void Classify_rejects_a_participant_outside_the_accepted_canton_minors(string reportedVersion)
        => Classify(reportedVersion).Should().Be(Verdict.Unsupported);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    [InlineData("v3.5.9")]
    public void Classify_rejects_a_version_it_cannot_parse(string? reportedVersion)
        => Classify(reportedVersion).Should().Be(Verdict.Unparseable);

    [Fact]
    public void AcceptedCantonMinors_contains_the_minor_of_the_vendored_spec()
        => AcceptedCantonMinors.Should().Contain(MajorMinorOf(VendoredSpecCantonVersion));

    [Fact]
    public void UnsupportedVersionFailureMessage_names_the_observed_and_vendored_versions_and_the_realignment_path()
        => UnsupportedVersionFailureMessage("3.4.9").Should()
            .Contain("3.4.9")
            .And.Contain(VendoredSpecCantonVersion)
            .And.Contain("src/Canton.Ledger.Rest/README.md");

    [Fact]
    public void UnsupportedVersionFailureMessage_states_the_lane_failed_rather_than_skipped()
        => UnsupportedVersionFailureMessage("3.4.9").Should().Contain("FAILED (not skipped)");

    [Fact]
    public void UnparseableVersionFailureMessage_names_the_unusable_version_and_calls_it_a_participant_defect()
        => UnparseableVersionFailureMessage("not-a-version").Should()
            .Contain("not-a-version")
            .And.Contain("defect");
}
