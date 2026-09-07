// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Xunit;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// Behavioural parity suite over the three interface-family members of
/// <see cref="ILedgerStreamer"/> — <c>SubscribeAsync</c>, <c>SubscribeActiveAsync</c> and
/// <c>SubscribeLedgerEffectsAsync</c> taking a <see cref="ViewDescriptor{TInterface, TView}"/> —
/// run against every provider that implements them through one shared set of test bodies. The
/// upstream conformance kit does not exercise these members, so this is where their cross-transport
/// contract is pinned: each yields the participant-computed view decoded into the descriptor's view
/// record, the snapshot ends with its terminal checkpoint, and the descriptor is a required
/// argument rather than a decorative one.
/// </summary>
public abstract class InterfaceSubscriptionParityTests
{
    /// <summary>The contract id every lane's participant reports for its one matching row.</summary>
    public const string ViewedContractId = "00viewed-holding";

    /// <summary>The amount the participant-computed view carries on that row.</summary>
    public const decimal ViewAmount = 42.5m;

    /// <summary>The offset that row is reported at, inside <see cref="Window"/>.</summary>
    public static LedgerOffset CreatedOffset { get; } = LedgerOffset.At(7);

    /// <summary>The synchronizer that row is scoped to.</summary>
    public static SynchronizerId Synchronizer { get; } = new("sync-1");

    /// <summary>The party the subscriptions are scoped to.</summary>
    public static Party Owner { get; } = new("party::alice");

    /// <summary>
    /// The bounded <c>(fromOffset, toOffset]</c> window the offset-range reads use. It is bounded
    /// because the REST transport serves no open-ended tail, and both bounds straddle
    /// <see cref="CreatedOffset"/> so the row lands strictly inside it.
    /// </summary>
    public static (LedgerOffset From, LedgerOffset To) Window { get; } = (LedgerOffset.Begin, LedgerOffset.At(10));

    /// <summary>
    /// Opens a streamer whose participant serves exactly one <see cref="IViewedInterfaceMarker"/>
    /// row — contract <see cref="ViewedContractId"/> at <see cref="CreatedOffset"/> on
    /// <see cref="Synchronizer"/>, carrying a computed view of <see cref="ViewAmount"/> — on all
    /// three interface reads.
    /// </summary>
    protected abstract Task<ILedgerStreamer> OpenInterfaceStreamerAsync();

    private static ViewDescriptor<IViewedInterfaceMarker, ViewedInterfaceView> View { get; } = new();

    [Fact]
    public async Task SubscribeActiveAsync_yields_the_decoded_view_then_its_terminal_checkpoint()
    {
        var streamer = await OpenInterfaceStreamerAsync();

        var entries = new List<InterfaceAcsSnapshotEntry<IViewedInterfaceMarker, ViewedInterfaceView>>();
        await foreach (var entry in streamer.SubscribeActiveAsync(
            View, Owner, cancellationToken: TestContext.Current.CancellationToken))
        {
            entries.Add(entry);
        }

        var created = entries[0].Should()
            .BeOfType<InterfaceAcsSnapshotEntry<IViewedInterfaceMarker, ViewedInterfaceView>.Created>().Subject;
        created.ContractId.Value.Should().Be(ViewedContractId);
        created.Payload.Amount.Should().Be(ViewAmount);
        created.SynchronizerId.Should().Be(Synchronizer);
        entries[^1].Should()
            .BeOfType<InterfaceAcsSnapshotEntry<IViewedInterfaceMarker, ViewedInterfaceView>.Checkpoint>();
    }

    [Fact]
    public async Task SubscribeAsync_yields_the_decoded_view_as_a_Created_over_the_bounded_window()
    {
        var streamer = await OpenInterfaceStreamerAsync();

        var created = await OnlyCreatedAsync(streamer.SubscribeAsync(
            View, Owner, Window.From, Window.To, TestContext.Current.CancellationToken));

        created.ContractId.Value.Should().Be(ViewedContractId);
        created.Payload.Amount.Should().Be(ViewAmount);
        created.Offset.Should().Be(CreatedOffset);
    }

    [Fact]
    public async Task SubscribeLedgerEffectsAsync_yields_the_decoded_view_as_a_Created_over_the_bounded_window()
    {
        var streamer = await OpenInterfaceStreamerAsync();

        var created = await OnlyCreatedAsync(streamer.SubscribeLedgerEffectsAsync(
            View, Owner, Window.From, Window.To, TestContext.Current.CancellationToken));

        created.ContractId.Value.Should().Be(ViewedContractId);
        created.Payload.Amount.Should().Be(ViewAmount);
        created.Offset.Should().Be(CreatedOffset);
    }

    [Fact]
    public async Task SubscribeActiveAsync_rejects_a_missing_view_descriptor()
    {
        var streamer = await OpenInterfaceStreamerAsync();

        var subscribing = () => streamer.SubscribeActiveAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            null!, Owner, cancellationToken: TestContext.Current.CancellationToken);

        subscribing.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task SubscribeAsync_rejects_a_missing_view_descriptor()
    {
        var streamer = await OpenInterfaceStreamerAsync();

        var subscribing = () => streamer.SubscribeAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            null!, Owner, Window.From, Window.To, TestContext.Current.CancellationToken);

        subscribing.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task SubscribeLedgerEffectsAsync_rejects_a_missing_view_descriptor()
    {
        var streamer = await OpenInterfaceStreamerAsync();

        var subscribing = () => streamer.SubscribeLedgerEffectsAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            null!, Owner, Window.From, Window.To, TestContext.Current.CancellationToken);

        subscribing.Should().Throw<ArgumentNullException>();
    }

    private static async Task<InterfaceStreamEvent<IViewedInterfaceMarker, ViewedInterfaceView>.Created> OnlyCreatedAsync(
        IAsyncEnumerable<InterfaceStreamEvent<IViewedInterfaceMarker, ViewedInterfaceView>> stream)
    {
        var events = new List<InterfaceStreamEvent<IViewedInterfaceMarker, ViewedInterfaceView>>();
        await foreach (var streamEvent in stream.WithCancellation(TestContext.Current.CancellationToken))
        {
            events.Add(streamEvent);
        }

        return events
            .OfType<InterfaceStreamEvent<IViewedInterfaceMarker, ViewedInterfaceView>.Created>()
            .Should().ContainSingle().Subject;
    }
}
