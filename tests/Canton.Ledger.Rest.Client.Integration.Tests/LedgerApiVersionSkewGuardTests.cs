// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace Canton.Ledger.Rest.Client.Integration.Tests;

public class LedgerApiVersionSkewGuardTests
{
    [Theory]
    [InlineData("3.4.11", LedgerApiVersionSkewGuard.Verdict.SameMinor)]
    [InlineData("3.4.0", LedgerApiVersionSkewGuard.Verdict.SameMinor)]
    [InlineData("3.4.20-snapshot.20260101.0", LedgerApiVersionSkewGuard.Verdict.SameMinor)]
    [InlineData("3.5.7", LedgerApiVersionSkewGuard.Verdict.MinorSkew)]
    [InlineData("3.3.0", LedgerApiVersionSkewGuard.Verdict.MinorSkew)]
    [InlineData("4.4.11", LedgerApiVersionSkewGuard.Verdict.MinorSkew)]
    [InlineData(null, LedgerApiVersionSkewGuard.Verdict.Unparseable)]
    [InlineData("", LedgerApiVersionSkewGuard.Verdict.Unparseable)]
    [InlineData("   ", LedgerApiVersionSkewGuard.Verdict.Unparseable)]
    [InlineData("not-a-version", LedgerApiVersionSkewGuard.Verdict.Unparseable)]
    [InlineData("v3.4.11", LedgerApiVersionSkewGuard.Verdict.Unparseable)]
    public void Classify_compares_the_reported_major_minor_against_the_vendored_spec(
        string? reportedVersion,
        LedgerApiVersionSkewGuard.Verdict expected)
        => Assert.Equal(expected, LedgerApiVersionSkewGuard.Classify(reportedVersion));
}
