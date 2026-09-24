// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestLedgerClientHonouredWindowHoldTests
{
    [Theory]
    [InlineData(2000, 1000)]
    [InlineData(250, 125)]
    [InlineData(10, 5)]
    [InlineData(20000, 10000)]
    public void A_window_counts_as_held_from_half_of_the_idle_timeout_the_participant_was_sent(
        int idleTimeoutMilliseconds, int expectedShortestHoldMilliseconds)
    {
        var shortestHold = RestLedgerClient.ShortestHonouredWindowHold(
            TimeSpan.FromMilliseconds(idleTimeoutMilliseconds));

        shortestHold.Should().Be(TimeSpan.FromMilliseconds(expectedShortestHoldMilliseconds));
    }
}
