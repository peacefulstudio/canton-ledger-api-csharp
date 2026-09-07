// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Telemetry;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class WellKnownActivitySourceNameTests
{
    [Fact]
    public void RestLedgerClient_full_type_name_matches_the_RestLedgerClient_constant() =>
        LedgerActivitySource.NameFor<RestLedgerClient>().Should().Be(LedgerActivitySourceNames.RestLedgerClient);
}
