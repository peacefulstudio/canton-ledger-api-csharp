// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Grpc.Client;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class WellKnownActivitySourceNameTests
{
    [Fact]
    public void LedgerClient_ActivitySourceName_matches_full_type_name() =>
        LedgerClient.ActivitySourceName.Should().Be(typeof(LedgerClient).FullName);

    [Fact]
    public void AdminClient_ActivitySourceName_matches_full_type_name() =>
        AdminClient.ActivitySourceName.Should().Be(typeof(AdminClient).FullName);
}
