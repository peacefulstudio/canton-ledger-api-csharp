// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class FakeLedgerClientAcsTerminalContractTests
{
    private static readonly Party Alice = new("alice");
    private static readonly SynchronizerId Sync = (SynchronizerId)"sync1";
    private static readonly DemoAsset Payload = new(Alice, Alice, "GOLD", 1m);

    [Fact]
    public void WithActiveContracts_rejects_a_snapshot_that_stages_no_terminal_entry()
    {
        var staging = () => FakeLedgerClient.Create().WithActiveContracts(CreatedAt(1));

        staging.Should().Throw<ArgumentException>()
            .WithMessage("*terminal*")
            .WithMessage("*WithMalformedActiveContracts*");
    }

    [Fact]
    public void WithActiveContracts_rejects_an_empty_snapshot()
    {
        var staging = () => FakeLedgerClient.Create().WithActiveContracts<DemoAsset>();

        staging.Should().Throw<ArgumentException>().WithMessage("*terminal*");
    }

    [Fact]
    public void WithActiveContracts_rejects_an_entry_staged_after_the_terminal_checkpoint()
    {
        var staging = () => FakeLedgerClient.Create().WithActiveContracts(
            CreatedAt(1),
            LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(2)),
            CreatedAt(3));

        staging.Should().Throw<ArgumentException>().WithMessage("*terminal*");
    }

    [Fact]
    public void WithActiveContracts_rejects_a_checkpoint_and_a_stream_error_in_the_same_snapshot()
    {
        var staging = () => FakeLedgerClient.Create().WithActiveContracts(
            LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(2)),
            LedgerEvents.StreamError<DemoAsset>(14, "snapshot aborted mid-stream"));

        staging.Should().Throw<ArgumentException>().WithMessage("*terminal*");
    }

    [Fact]
    public void WithActiveContracts_accepts_a_snapshot_ending_on_its_terminal_checkpoint()
    {
        var staging = () => FakeLedgerClient.Create().WithActiveContracts(
            CreatedAt(1),
            LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(2)));

        staging.Should().NotThrow();
    }

    [Fact]
    public void WithActiveContracts_accepts_a_snapshot_ending_on_a_terminal_stream_error()
    {
        var staging = () => FakeLedgerClient.Create().WithActiveContracts(
            CreatedAt(1),
            LedgerEvents.StreamError<DemoAsset>(14, "snapshot aborted mid-stream"));

        staging.Should().NotThrow();
    }

    [Fact]
    public async Task WithMalformedActiveContracts_replays_a_snapshot_the_real_ledger_cannot_produce()
    {
        var truncated = CreatedAt(1);
        var client = FakeLedgerClient.Create().WithMalformedActiveContracts(truncated).Build();

        var entries = await CollectAsync(client.SubscribeActiveAsync<DemoAsset>(
            Alice, cancellationToken: TestContext.Current.CancellationToken));

        entries.Should().Equal(truncated);
    }

    [Fact]
    public async Task WithMalformedActiveContracts_replaces_a_contract_shaped_snapshot_staged_for_the_same_type()
    {
        var truncated = CreatedAt(1);
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(CreatedAt(1), LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(2)))
            .WithMalformedActiveContracts(truncated)
            .Build();

        var entries = await CollectAsync(client.SubscribeActiveAsync<DemoAsset>(
            Alice, cancellationToken: TestContext.Current.CancellationToken));

        entries.Should().Equal(truncated);
    }

    [Fact]
    public void WithActiveInterfaceContracts_rejects_a_snapshot_that_stages_no_terminal_entry()
    {
        var staging = () => FakeLedgerClient.Create()
            .WithActiveInterfaceContracts<IDemoHoldingView, DemoHoldingView>(HoldingCreatedAt(1));

        staging.Should().Throw<ArgumentException>()
            .WithMessage("*terminal*")
            .WithMessage("*WithMalformedActiveInterfaceContracts*");
    }

    [Fact]
    public void WithActiveInterfaceContracts_rejects_an_entry_staged_after_the_terminal_checkpoint()
    {
        var staging = () => FakeLedgerClient.Create()
            .WithActiveInterfaceContracts<IDemoHoldingView, DemoHoldingView>(
                HoldingCheckpointAt(1),
                HoldingCreatedAt(2));

        staging.Should().Throw<ArgumentException>().WithMessage("*terminal*");
    }

    [Fact]
    public void WithActiveInterfaceContracts_accepts_a_snapshot_ending_on_its_terminal_checkpoint()
    {
        var staging = () => FakeLedgerClient.Create()
            .WithActiveInterfaceContracts<IDemoHoldingView, DemoHoldingView>(
                HoldingCreatedAt(1),
                HoldingCheckpointAt(1));

        staging.Should().NotThrow();
    }

    [Fact]
    public async Task WithMalformedActiveInterfaceContracts_replays_a_snapshot_the_real_ledger_cannot_produce()
    {
        var truncated = HoldingCreatedAt(1);
        var client = FakeLedgerClient.Create()
            .WithMalformedActiveInterfaceContracts<IDemoHoldingView, DemoHoldingView>(truncated)
            .Build();

        var entries = await CollectAsync(client.SubscribeActiveAsync(
            new ViewDescriptor<IDemoHoldingView, DemoHoldingView>(),
            Alice,
            cancellationToken: TestContext.Current.CancellationToken));

        entries.Should().Equal(truncated);
    }

    private static AcsSnapshotEntry<DemoAsset> CreatedAt(long offset) =>
        LedgerEvents.Created(
            new ContractId<DemoAsset>($"cid{offset}"), Payload, null, LedgerOffset.At(offset), Sync, [Alice]);

    private static InterfaceAcsSnapshotEntry<IDemoHoldingView, DemoHoldingView> HoldingCreatedAt(long offset) =>
        new InterfaceAcsSnapshotEntry<IDemoHoldingView, DemoHoldingView>.Created(
            new ContractId<IDemoHoldingView>($"cid{offset}"),
            new DemoHoldingView(offset),
            null,
            LedgerOffset.At(offset),
            Sync,
            [Alice]);

    private static InterfaceAcsSnapshotEntry<IDemoHoldingView, DemoHoldingView> HoldingCheckpointAt(long offset) =>
        new InterfaceAcsSnapshotEntry<IDemoHoldingView, DemoHoldingView>.Checkpoint(
            new StakeholderResume(LedgerOffset.At(offset)));

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        var items = new List<T>();
        await foreach (var item in source)
        {
            items.Add(item);
        }

        return items;
    }
}
