// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Richtypes;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Behavioral parity suite over <see cref="ILedgerStreamer"/>, run against every provider that
/// implements it (the in-memory Fake, REST, and gRPC) through one shared set of test bodies. Each
/// lane creates a <see cref="Marker"/> contract through the <see cref="ILedgerWriter"/> capability,
/// then confirms the streaming reads surface that contract the same way regardless of transport.
/// </summary>
public abstract class LedgerStreamerParityTests
{
    /// <summary>
    /// Whether this provider's ledger end advances across a write, so that a contract created
    /// between two <see cref="ILedgerReader.GetLedgerEndAsync"/> reads lands strictly inside the
    /// resulting <c>(fromOffset, toOffset]</c> window. Lanes whose ledger end is static cannot
    /// express a bounded range around a write at all and opt out of the two checks below.
    /// </summary>
    protected virtual bool SupportsAdvancingLedgerEnd => true;

    /// <summary>Opens a lane over this provider's reader/writer/streamer capabilities for one test.</summary>
    protected abstract Task<CapabilityLane<(ILedgerReader Reader, ILedgerWriter Writer, ILedgerStreamer Streamer, Party Owner)>>
        OpenStreamerAsync(CancellationToken cancellationToken);

    [Fact]
    public async Task SubscribeActiveAsync_returns_a_snapshot_containing_the_created_Marker_and_ends_with_a_checkpoint()
    {
        await using var lane = await OpenStreamerAsync(TestContext.Current.CancellationToken);
        var (reader, writer, streamer, owner) = lane.Capability;

        var markerCid = await CreateMarkerAsync(writer, owner);
        var ledgerEnd = await reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var entries = new List<AcsSnapshotEntry<Marker>>();
        await foreach (var entry in streamer.SubscribeActiveAsync<Marker>(
            owner, ledgerEnd, TestContext.Current.CancellationToken))
        {
            entries.Add(entry);
        }

        entries.OfType<AcsSnapshotEntry<Marker>.Created>().Should().Contain(created => created.ContractId.Equals(markerCid));
        entries[^1].Should().BeOfType<AcsSnapshotEntry<Marker>.Checkpoint>();
    }

    [Fact]
    public async Task SubscribeAsync_returns_the_created_Marker_as_an_ACS_delta_event_over_the_bounded_range()
    {
        Assert.SkipUnless(
            SupportsAdvancingLedgerEnd,
            "this lane's ledger end does not advance across a write, so the bounded range is empty by construction");

        await using var lane = await OpenStreamerAsync(TestContext.Current.CancellationToken);
        var (reader, writer, streamer, owner) = lane.Capability;

        await CreateMarkerBeforeTheWindowAsync(writer, owner);
        var fromOffset = await reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        var markerCid = await CreateMarkerAsync(writer, owner);
        var toOffset = await reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var events = new List<ContractStreamEvent<Marker>>();
        await foreach (var evt in streamer.SubscribeAsync<Marker>(
            owner, fromOffset, toOffset, TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }

        events.OfType<ContractStreamEvent<Marker>.Created>().Should().Contain(created => created.ContractId.Equals(markerCid));
        events.OfType<ContractStreamEvent<Marker>.Created>().Should().OnlyContain(
            created => created.Offset.Value > fromOffset.Value && created.Offset.Value <= toOffset.Value);
    }

    [Fact]
    public async Task SubscribeLedgerEffectsAsync_returns_the_created_Marker_as_a_ledger_effects_event_over_the_bounded_range()
    {
        Assert.SkipUnless(
            SupportsAdvancingLedgerEnd,
            "this lane's ledger end does not advance across a write, so the bounded range is empty by construction");

        await using var lane = await OpenStreamerAsync(TestContext.Current.CancellationToken);
        var (reader, writer, streamer, owner) = lane.Capability;

        await CreateMarkerBeforeTheWindowAsync(writer, owner);
        var fromOffset = await reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        var markerCid = await CreateMarkerAsync(writer, owner);
        var toOffset = await reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var events = new List<ContractStreamEvent<Marker>>();
        await foreach (var evt in streamer.SubscribeLedgerEffectsAsync<Marker>(
            owner, fromOffset, toOffset, TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }

        events.OfType<ContractStreamEvent<Marker>.Created>().Should().Contain(created => created.ContractId.Equals(markerCid));
        events.OfType<ContractStreamEvent<Marker>.Created>().Should().OnlyContain(
            created => created.Offset.Value > fromOffset.Value && created.Offset.Value <= toOffset.Value);
    }

    private static Task CreateMarkerBeforeTheWindowAsync(ILedgerWriter writer, Party owner) =>
        CreateMarkerAsync(writer, owner);

    private static async Task<ContractId<Marker>> CreateMarkerAsync(ILedgerWriter writer, Party owner)
    {
        var outcome = await writer.TryCreateAsync(
            new Marker(owner), owner, cancellationToken: TestContext.Current.CancellationToken);
        return outcome.Should().BeOfType<ExerciseOutcome<ContractId<Marker>>.One>().Subject.Result;
    }
}
