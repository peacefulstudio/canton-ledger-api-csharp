// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Testing;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Richtypes;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class FakeLedgerWriterParityTests : LedgerWriterParityTests
{
    protected override Task<CapabilityLane<(ILedgerWriter Writer, Party Owner)>> OpenWriterAsync(
        CancellationToken cancellationToken)
    {
        var owner = new Party("fake::marker-owner");
        var client = FakeLedgerClient.Create()
            .WithCreateResult<Marker>(new ExerciseOutcome<ContractId<Marker>>.One(new ContractId<Marker>("00fake-marker")))
            .WithExerciseResult<DamlUnit>(new ExerciseOutcome<DamlUnit>.One(DamlUnit.Instance))
            .Build();

        return Task.FromResult(new CapabilityLane<(ILedgerWriter, Party)>((client, owner), client.DisposeAsync));
    }
}
