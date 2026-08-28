// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Testing;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Richtypes;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class FakeLedgerStreamerParityTests : LedgerStreamerParityTests
{
    protected override Task<CapabilityLane<(ILedgerReader Reader, ILedgerWriter Writer, ILedgerStreamer Streamer, Party Owner)>>
        OpenStreamerAsync(CancellationToken cancellationToken)
    {
        var owner = new Party("fake::marker-owner");
        var markerCid = new ContractId<Marker>("00fake-marker");
        var synchronizerId = new SynchronizerId("fake::sync-1");
        var ledgerEndBeforeAnyWrite = LedgerOffset.At(1);
        var snapshotCreatedAt = LedgerOffset.At(2);
        var createdInsideTheWindowAt = LedgerOffset.At(3);
        var markerAtTheWindowsExcludedLowerBound = MarkerCreatedAt(
            new ContractId<Marker>("00fake-marker-at-lower-bound"), owner, synchronizerId, snapshotCreatedAt);
        var markerOneOffsetPastTheWindow = MarkerCreatedAt(
            new ContractId<Marker>("00fake-marker-past-upper-bound"), owner, synchronizerId, LedgerOffset.At(4));

        var client = FakeLedgerClient.Create()
            .WithLedgerEnd(ledgerEndBeforeAnyWrite)
            .WithCreateResult<Marker>(new ExerciseOutcome<ContractId<Marker>>.One(markerCid))
            .WithActiveContracts<Marker>(
                new AcsSnapshotEntry<Marker>.Created(markerCid, new Marker(owner).ToRecord(), snapshotCreatedAt, synchronizerId, [owner]),
                new AcsSnapshotEntry<Marker>.Checkpoint(new StakeholderResume(snapshotCreatedAt)))
            .WithContractEvents<Marker>(
                markerAtTheWindowsExcludedLowerBound,
                MarkerCreatedAt(markerCid, owner, synchronizerId, createdInsideTheWindowAt),
                markerOneOffsetPastTheWindow)
            .WithLedgerEffects<Marker>(
                markerAtTheWindowsExcludedLowerBound,
                MarkerCreatedAt(markerCid, owner, synchronizerId, createdInsideTheWindowAt),
                markerOneOffsetPastTheWindow)
            .Build();

        return Task.FromResult(
            new CapabilityLane<(ILedgerReader, ILedgerWriter, ILedgerStreamer, Party)>(
                (client, client, client, owner), client.DisposeAsync));
    }

    private static ContractStreamEvent<Marker>.Created MarkerCreatedAt(
        ContractId<Marker> contractId, Party owner, SynchronizerId synchronizerId, LedgerOffset offset) =>
        new(contractId, new Marker(owner).ToRecord(), offset, synchronizerId, [owner]);
}
