// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Testing;
using Daml.Ledger.Abstractions;
using Daml.Runtime;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class FakeLedgerReaderParityTests : LedgerReaderParityTests
{
    protected override Task<CapabilityLane<ILedgerReader>> OpenReaderAsync(CancellationToken cancellationToken)
    {
        var client = FakeLedgerClient.Create().WithLedgerEnd(LedgerOffset.At(42)).Build();
        return Task.FromResult(new CapabilityLane<ILedgerReader>(client, client.DisposeAsync));
    }
}
