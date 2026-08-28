// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Rest.Client;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class WellKnownActivitySourceNameTests
{
    [Fact]
    public void RestLedgerClient_ActivitySourceName_matches_full_type_name() =>
        RestLedgerClient.ActivitySourceName.Should().Be(typeof(RestLedgerClient).FullName);
}
