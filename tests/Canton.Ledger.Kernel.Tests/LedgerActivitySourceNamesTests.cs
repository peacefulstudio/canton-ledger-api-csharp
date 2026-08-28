// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Telemetry;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public class LedgerActivitySourceNamesTests
{
    [Theory]
    [InlineData("Canton.Ledger.Grpc.Client.LedgerClient")]
    [InlineData("Canton.Ledger.Grpc.Client.AdminClient")]
    [InlineData("Canton.Ledger.Rest.Client.RestLedgerClient")]
    [InlineData("Canton.Ledger.Pqs.Client.PqsClient")]
    public void All_carries_every_well_known_client_source(string expectedName) =>
        LedgerActivitySourceNames.All.Should().Contain(expectedName);

    [Fact]
    public void All_carries_nothing_beyond_the_four_client_sources() =>
        LedgerActivitySourceNames.All.Should().HaveCount(4);

    [Fact]
    public void All_names_are_distinct() =>
        LedgerActivitySourceNames.All.Should().OnlyHaveUniqueItems();
}
