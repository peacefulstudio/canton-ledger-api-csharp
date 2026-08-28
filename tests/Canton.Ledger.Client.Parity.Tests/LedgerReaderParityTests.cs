// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Ledger.Abstractions;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Behavioral parity suite over <see cref="ILedgerReader"/>, run against every provider that
/// implements it (the in-memory Fake, REST, and gRPC) through one shared set of test bodies.
/// </summary>
public abstract class LedgerReaderParityTests
{
    /// <summary>Opens a lane over this provider's <see cref="ILedgerReader"/> for one test.</summary>
    protected abstract Task<CapabilityLane<ILedgerReader>> OpenReaderAsync(CancellationToken cancellationToken);

    [Fact]
    public async Task GetLedgerEndAsync_returns_a_non_negative_offset()
    {
        await using var lane = await OpenReaderAsync(TestContext.Current.CancellationToken);

        var end = await lane.Capability.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        end.Value.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetLedgerEndAsync_does_not_regress_across_two_consecutive_reads()
    {
        await using var lane = await OpenReaderAsync(TestContext.Current.CancellationToken);

        var first = await lane.Capability.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        var second = await lane.Capability.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        second.Value.Should().BeGreaterThanOrEqualTo(first.Value);
    }
}
