// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using RichTypes;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class FakeLedgerTransactionTreeParityTests : LedgerTransactionTreeParityTests
{
    protected override Task<CapabilityLane<(ICantonLedgerClient Client, Party Owner)>> OpenTransactionTreeAsync(
        CancellationToken cancellationToken)
    {
        var owner = new Party("fake::tree-owner");
        var created = new TreeEvent.Created(
            EventId: "1",
            ContractId: "00fake-marker",
            TemplateId: Marker.TemplateId,
            CreateArguments: new Marker(owner).ToRecord(),
            WitnessParties: [owner],
            Signatories: [owner],
            Observers: [],
            ContractKey: null,
            CreatedAt: DateTimeOffset.UnixEpoch);

        var committed = new TransactionTree("fake-tree-update-1", LedgerOffset.At(1), [created]);

        var client = FakeLedgerClient.Create()
            .WithTransactionTree(LedgerOutcomes.One(committed))
            .WithUpdateTreeByOffset(committed.CompletionOffset.Value, committed)
            .Build();

        return Task.FromResult(
            new CapabilityLane<(ICantonLedgerClient, Party)>((client, owner), client.DisposeAsync));
    }
}
