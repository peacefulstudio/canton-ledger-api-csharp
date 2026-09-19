// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using RichTypes;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class FakeLedgerStreamerParityTests : LedgerStreamerParityTests
{
    protected override bool SupportsParticipantRejectedReads => false;

    protected override Task<CapabilityLane<(ILedgerReader Reader, ILedgerWriter Writer, ICantonLedgerClient Client, Party Owner)>>
        OpenStreamerAsync(CancellationToken cancellationToken)
    {
        var owner = new Party("fake::marker-owner");
        var markerCid = new ContractId<Marker>("00fake-marker");
        var assetCid = new ContractId<Asset>("00fake-asset");
        var holdingCid = new ContractId<IHolding>(assetCid.Value);
        var holdingView = new HoldingView(AssetAmount);
        var synchronizerId = new SynchronizerId("fake::sync-1");
        var ledgerEndBeforeAnyWrite = LedgerOffset.At(1);
        var snapshotCreatedAt = LedgerOffset.At(2);
        var createdInsideTheWindowAt = LedgerOffset.At(3);
        var markerAtTheWindowsExcludedLowerBound = MarkerCreatedAt(
            new ContractId<Marker>("00fake-marker-at-lower-bound"), owner, synchronizerId, snapshotCreatedAt);
        var markerOneOffsetPastTheWindow = MarkerCreatedAt(
            new ContractId<Marker>("00fake-marker-past-upper-bound"), owner, synchronizerId, LedgerOffset.At(4));
        var holdingAtTheWindowsExcludedLowerBound = HoldingCreatedAt(
            new ContractId<IHolding>("00fake-asset-at-lower-bound"), holdingView, owner, synchronizerId, snapshotCreatedAt);
        var holdingOneOffsetPastTheWindow = HoldingCreatedAt(
            new ContractId<IHolding>("00fake-asset-past-upper-bound"), holdingView, owner, synchronizerId, LedgerOffset.At(4));

        var client = FakeLedgerClient.Create()
            .WithLedgerEnd(ledgerEndBeforeAnyWrite)
            .WithCreateResult<Marker>(new ExerciseOutcome<ContractId<Marker>>.One(markerCid))
            .WithCreateResult<Asset>(new ExerciseOutcome<ContractId<Asset>>.One(assetCid))
            .WithActiveContracts<Marker>(
                new AcsSnapshotEntry<Marker>.Created(markerCid, new Marker(owner), null, snapshotCreatedAt, synchronizerId, [owner]),
                new AcsSnapshotEntry<Marker>.Checkpoint(new StakeholderResume(snapshotCreatedAt)))
            .WithContractEvents<Marker>(
                markerAtTheWindowsExcludedLowerBound,
                MarkerCreatedAt(markerCid, owner, synchronizerId, createdInsideTheWindowAt),
                markerOneOffsetPastTheWindow)
            .WithLedgerEffects<Marker>(
                markerAtTheWindowsExcludedLowerBound,
                MarkerCreatedAt(markerCid, owner, synchronizerId, createdInsideTheWindowAt),
                markerOneOffsetPastTheWindow)
            .WithActiveInterfaceContracts<IHolding, HoldingView>(
                new InterfaceAcsSnapshotEntry<IHolding, HoldingView>.Created(
                    holdingCid, holdingView, null, snapshotCreatedAt, synchronizerId, [owner]),
                new InterfaceAcsSnapshotEntry<IHolding, HoldingView>.Checkpoint(new StakeholderResume(snapshotCreatedAt)))
            .WithInterfaceEvents<IHolding, HoldingView>(
                holdingAtTheWindowsExcludedLowerBound,
                HoldingCreatedAt(holdingCid, holdingView, owner, synchronizerId, createdInsideTheWindowAt),
                holdingOneOffsetPastTheWindow)
            .WithInterfaceLedgerEffects<IHolding, HoldingView>(
                holdingAtTheWindowsExcludedLowerBound,
                HoldingCreatedAt(holdingCid, holdingView, owner, synchronizerId, createdInsideTheWindowAt),
                holdingOneOffsetPastTheWindow)
            .Build();

        return Task.FromResult(
            new CapabilityLane<(ILedgerReader, ILedgerWriter, ICantonLedgerClient, Party)>(
                (client, client, client, owner), client.DisposeAsync));
    }

    private static ContractStreamEvent<Marker>.Created MarkerCreatedAt(
        ContractId<Marker> contractId, Party owner, SynchronizerId synchronizerId, LedgerOffset offset) =>
        new(contractId, new Marker(owner), null, offset, synchronizerId, [owner]);

    private static InterfaceStreamEvent<IHolding, HoldingView>.Created HoldingCreatedAt(
        ContractId<IHolding> contractId,
        HoldingView view,
        Party owner,
        SynchronizerId synchronizerId,
        LedgerOffset offset) =>
        new(contractId, view, null, offset, synchronizerId, [owner]);
}
