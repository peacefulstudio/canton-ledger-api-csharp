// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing;
using Daml.Runtime.Data;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class FakeLedgerTrafficCostParityTests : LedgerTrafficCostParityTests
{
    protected override Task<CapabilityLane<(ICantonLedgerClient Client, Party Owner)>> OpenTrafficCostAsync(
        CancellationToken cancellationToken)
    {
        var owner = new Party("fake::pricing-owner");
        var client = FakeLedgerClient.Create()
            .WithTrafficCostEstimate(new TrafficCostEstimate(
                DateTimeOffset.UnixEpoch,
                ConfirmationRequestCost: 1_024,
                ConfirmationResponseCost: 256,
                TotalCost: 1_280))
            .Build();

        return Task.FromResult(
            new CapabilityLane<(ICantonLedgerClient, Party)>((client, owner), client.DisposeAsync));
    }
}
