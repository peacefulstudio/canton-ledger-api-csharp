// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Telemetry;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class WellKnownActivitySourceNameTests
{
    [Fact]
    public void LedgerClient_full_type_name_matches_the_GrpcLedgerClient_constant() =>
        LedgerActivitySource.NameFor<LedgerClient>().Should().Be(LedgerActivitySourceNames.GrpcLedgerClient);

    [Fact]
    public void AdminClient_full_type_name_matches_the_GrpcAdminClient_constant() =>
        LedgerActivitySource.NameFor<AdminClient>().Should().Be(LedgerActivitySourceNames.GrpcAdminClient);
}
