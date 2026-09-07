// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Testing.Helpers;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Testing.Tests;

public sealed class FakeInterfaceSubscriptionParityTests : InterfaceSubscriptionParityTests
{
    protected override Task<ILedgerStreamer> OpenInterfaceStreamerAsync()
    {
        ILedgerStreamer streamer = FakeLedgerClient.Create()
            .WithLedgerEnd(Window.To)
            .WithActiveInterfaceContracts<IViewedInterfaceMarker, ViewedInterfaceView>(
                new InterfaceAcsSnapshotEntry<IViewedInterfaceMarker, ViewedInterfaceView>.Created(
                    Holding, View, Key: null, CreatedOffset, Synchronizer, [Owner]),
                new InterfaceAcsSnapshotEntry<IViewedInterfaceMarker, ViewedInterfaceView>.Checkpoint(
                    new StakeholderResume(CreatedOffset)))
            .WithInterfaceEvents<IViewedInterfaceMarker, ViewedInterfaceView>(HoldingCreated)
            .WithInterfaceLedgerEffects<IViewedInterfaceMarker, ViewedInterfaceView>(HoldingCreated)
            .Build();

        return Task.FromResult(streamer);
    }

    private static readonly ContractId<IViewedInterfaceMarker> Holding = new(ViewedContractId);

    private static readonly ViewedInterfaceView View = new(ViewAmount);

    private static readonly InterfaceStreamEvent<IViewedInterfaceMarker, ViewedInterfaceView>.Created HoldingCreated =
        new(Holding, View, Key: null, CreatedOffset, Synchronizer, [Owner]);
}
