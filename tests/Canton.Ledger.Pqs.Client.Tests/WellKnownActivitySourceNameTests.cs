// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Telemetry;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class WellKnownActivitySourceNameTests
{
    [Fact]
    public void PqsClient_full_type_name_matches_the_PqsClient_constant() =>
        LedgerActivitySource.NameFor<PqsClient>().Should().Be(LedgerActivitySourceNames.PqsClient);
}
