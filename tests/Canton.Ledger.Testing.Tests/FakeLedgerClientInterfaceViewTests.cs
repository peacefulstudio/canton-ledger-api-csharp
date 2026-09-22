// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class FakeLedgerClientInterfaceViewTests
{
    private static readonly Party Owner = new("bob");

    private static readonly SynchronizerId Synchronizer = (SynchronizerId)"sync1";

    [Fact]
    public async Task QueryActiveAsync_returns_every_staged_interface_view()
    {
        ICantonLedgerClient client = FakeLedgerClient.Create()
            .WithActiveInterfaceContracts<IDemoHoldingView, DemoHoldingView>(
                Created("cid1", 42.5m, 1),
                Created("cid2", 7m, 2),
                Checkpoint(2))
            .Build();

        var holdings = await client.QueryActiveAsync<IDemoHoldingView, DemoHoldingView>(
            Owner, cancellationToken: TestContext.Current.CancellationToken);

        holdings.Should().HaveCount(2);
        holdings[0].Contract.Id.Value.Should().Be("cid1");
        holdings[0].Contract.View.Amount.Should().Be(42.5m);
        holdings[0].LastUpdateOffset.Should().Be(LedgerOffset.At(1));
        holdings[0].SynchronizerId.Should().Be(Synchronizer);
        holdings[1].Contract.Id.Value.Should().Be("cid2");
        holdings[1].Contract.View.Amount.Should().Be(7m);
        holdings[1].LastUpdateOffset.Should().Be(LedgerOffset.At(2));
        holdings[1].SynchronizerId.Should().Be(Synchronizer);
    }

    [Fact]
    public async Task QueryActiveAsync_returns_an_empty_list_for_a_checkpoint_only_snapshot()
    {
        ICantonLedgerClient client = FakeLedgerClient.Create()
            .WithActiveInterfaceContracts<IDemoHoldingView, DemoHoldingView>(Checkpoint(9))
            .Build();

        var holdings = await client.QueryActiveAsync<IDemoHoldingView, DemoHoldingView>(
            Owner, cancellationToken: TestContext.Current.CancellationToken);

        holdings.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryActiveAsync_throws_LedgerOperationException_when_the_snapshot_has_no_terminal_checkpoint()
    {
        ICantonLedgerClient client = FakeLedgerClient.Create()
            .WithMalformedActiveInterfaceContracts<IDemoHoldingView, DemoHoldingView>(Created("cid1", 1m, 1))
            .Build();

        var querying = async () => await client.QueryActiveAsync<IDemoHoldingView, DemoHoldingView>(
            Owner, cancellationToken: TestContext.Current.CancellationToken);

        await querying.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage("*without its terminal checkpoint*");
    }

    [Fact]
    public async Task QueryActiveAsync_throws_LedgerOperationException_carrying_the_status_code_when_the_snapshot_faults()
    {
        ICantonLedgerClient client = FakeLedgerClient.Create()
            .WithActiveInterfaceContracts<IDemoHoldingView, DemoHoldingView>(
                new InterfaceAcsSnapshotEntry<IDemoHoldingView, DemoHoldingView>.StreamError(
                    14, "snapshot aborted mid-stream"))
            .Build();

        var querying = async () => await client.QueryActiveAsync<IDemoHoldingView, DemoHoldingView>(
            Owner, cancellationToken: TestContext.Current.CancellationToken);

        (await querying.Should().ThrowAsync<LedgerOperationException>())
            .Which.StatusCode.Should().Be(14);
    }

    [Fact]
    public async Task QueryActiveAsync_throws_LedgerOperationException_when_the_snapshot_carries_an_unclassified_row()
    {
        ICantonLedgerClient client = FakeLedgerClient.Create()
            .WithActiveInterfaceContracts<IDemoHoldingView, DemoHoldingView>(
                new InterfaceAcsSnapshotEntry<IDemoHoldingView, DemoHoldingView>.Unclassified(
                    LedgerOffset.At(1), UnclassifiedKind.InterfaceViewUnavailable),
                Checkpoint(1))
            .Build();

        var querying = async () => await client.QueryActiveAsync<IDemoHoldingView, DemoHoldingView>(
            Owner, cancellationToken: TestContext.Current.CancellationToken);

        await querying.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage("*unclassified row*");
    }

    [Fact]
    public async Task QueryActiveAsync_keeps_each_interface_marker_on_its_own_staged_snapshot()
    {
        ICantonLedgerClient client = FakeLedgerClient.Create()
            .WithActiveInterfaceContracts<IDemoHoldingView, DemoHoldingView>(Created("cid1", 3m, 1), Checkpoint(1))
            .WithActiveInterfaceContracts<IKeyedHoldingView, KeyedHoldingView>(
                new InterfaceAcsSnapshotEntry<IKeyedHoldingView, KeyedHoldingView>.Created(
                    new ContractId<IKeyedHoldingView>("cid2"),
                    new KeyedHoldingView(9m),
                    null,
                    LedgerOffset.At(2),
                    Synchronizer,
                    [Owner]),
                new InterfaceAcsSnapshotEntry<IKeyedHoldingView, KeyedHoldingView>.Checkpoint(
                    new StakeholderResume(LedgerOffset.At(2))))
            .Build();

        var demo = await client.QueryActiveAsync<IDemoHoldingView, DemoHoldingView>(
            Owner, cancellationToken: TestContext.Current.CancellationToken);
        var keyed = await client.QueryActiveAsync<IKeyedHoldingView, KeyedHoldingView>(
            Owner, cancellationToken: TestContext.Current.CancellationToken);

        demo.Should().ContainSingle().Which.Contract.View.Amount.Should().Be(3m);
        keyed.Should().ContainSingle().Which.Contract.View.Amount.Should().Be(9m);
    }

    [Fact]
    public async Task QueryActiveAsync_carries_the_contract_key_for_a_keyed_template_observed_through_an_interface()
    {
        var key = new ContractKey(new DamlParty((string)Owner), new Identifier("test-tpkg", "MiniDemo.Keyed", "Keyed"));

        ICantonLedgerClient client = FakeLedgerClient.Create()
            .WithActiveInterfaceContracts<IKeyedHoldingView, KeyedHoldingView>(
                new InterfaceAcsSnapshotEntry<IKeyedHoldingView, KeyedHoldingView>.Created(
                    new ContractId<IKeyedHoldingView>("cid1"),
                    new KeyedHoldingView(5m),
                    key,
                    LedgerOffset.At(1),
                    Synchronizer,
                    [Owner]),
                new InterfaceAcsSnapshotEntry<IKeyedHoldingView, KeyedHoldingView>.Checkpoint(
                    new StakeholderResume(LedgerOffset.At(1))))
            .Build();

        var holdings = await client.QueryActiveAsync<IKeyedHoldingView, KeyedHoldingView>(
            Owner, cancellationToken: TestContext.Current.CancellationToken);

        holdings.Should().ContainSingle().Which.Contract.Key.Should().Be(key);
    }

    private static InterfaceAcsSnapshotEntry<IDemoHoldingView, DemoHoldingView> Created(
        string contractId, decimal amount, long offset) =>
        new InterfaceAcsSnapshotEntry<IDemoHoldingView, DemoHoldingView>.Created(
            new ContractId<IDemoHoldingView>(contractId),
            new DemoHoldingView(amount),
            null,
            LedgerOffset.At(offset),
            Synchronizer,
            [Owner]);

    private static InterfaceAcsSnapshotEntry<IDemoHoldingView, DemoHoldingView> Checkpoint(long offset) =>
        new InterfaceAcsSnapshotEntry<IDemoHoldingView, DemoHoldingView>.Checkpoint(
            new StakeholderResume(LedgerOffset.At(offset)));
}
