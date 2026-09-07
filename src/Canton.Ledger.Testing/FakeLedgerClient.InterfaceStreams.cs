// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Testing;

public sealed partial class FakeLedgerClient
{
    /// <inheritdoc />
    /// <remarks>
    /// Replays the snapshot staged through
    /// <see cref="FakeLedgerClientBuilder.WithActiveInterfaceContracts{TInterface, TView}"/> for
    /// <typeparamref name="TInterface"/>, dropping the rows past
    /// <paramref name="activeAtOffset"/> exactly as the template-family
    /// <see cref="SubscribeActiveAsync{T}"/> does. Leaving the interface unstaged throws a
    /// <see cref="NotSupportedException"/> naming the missing setup.
    /// </remarks>
    public IAsyncEnumerable<InterfaceAcsSnapshotEntry<TInterface, TView>> SubscribeActiveAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        ArgumentNullException.ThrowIfNull(view);

        return Replay(
            ActiveAt(
                Staged<IReadOnlyList<InterfaceAcsSnapshotEntry<TInterface, TView>>>(
                    _interfaces.ActiveContracts,
                    typeof(TInterface),
                    "active interface-view snapshot",
                    $"WithActiveInterfaceContracts<{typeof(TInterface).Name}, {typeof(TView).Name}>"),
                activeAtOffset),
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Replays the events staged through
    /// <see cref="FakeLedgerClientBuilder.WithInterfaceEvents{TInterface, TView}"/> for
    /// <typeparamref name="TInterface"/>, bounded to <c>(fromOffset, toOffset]</c> exactly as the
    /// template-family <see cref="SubscribeAsync{T}"/> does.
    /// </remarks>
    public IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        ArgumentNullException.ThrowIfNull(view);

        return Replay(
            Within(
                Staged<IReadOnlyList<InterfaceStreamEvent<TInterface, TView>>>(
                    _interfaces.StreamEvents,
                    typeof(TInterface),
                    "interface stream",
                    $"WithInterfaceEvents<{typeof(TInterface).Name}, {typeof(TView).Name}>"),
                fromOffset,
                toOffset),
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Replays the events staged through
    /// <see cref="FakeLedgerClientBuilder.WithInterfaceLedgerEffects{TInterface, TView}"/> for
    /// <typeparamref name="TInterface"/>, bounded to <c>(fromOffset, toOffset]</c> exactly as the
    /// template-family <see cref="SubscribeLedgerEffectsAsync{T}"/> does.
    /// </remarks>
    public IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeLedgerEffectsAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        ArgumentNullException.ThrowIfNull(view);

        return Replay(
            Within(
                Staged<IReadOnlyList<InterfaceStreamEvent<TInterface, TView>>>(
                    _interfaces.LedgerEffects,
                    typeof(TInterface),
                    "interface ledger-effects stream",
                    $"WithInterfaceLedgerEffects<{typeof(TInterface).Name}, {typeof(TView).Name}>"),
                fromOffset,
                toOffset),
            cancellationToken);
    }

    private static IReadOnlyList<InterfaceAcsSnapshotEntry<TInterface, TView>> ActiveAt<TInterface, TView>(
        IReadOnlyList<InterfaceAcsSnapshotEntry<TInterface, TView>> entries,
        LedgerOffset? activeAtOffset)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        activeAtOffset is not { } snapshotOffset
            ? entries
            : entries.Where(entry => OffsetOf(entry) is not { } at || at.Value <= snapshotOffset.Value).ToArray();

    private static IReadOnlyList<InterfaceStreamEvent<TInterface, TView>> Within<TInterface, TView>(
        IReadOnlyList<InterfaceStreamEvent<TInterface, TView>> events,
        LedgerOffset? fromOffset,
        LedgerOffset? toOffset)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        fromOffset is null && toOffset is null
            ? events
            : events.Where(streamEvent => IsWithin(OffsetOf(streamEvent), fromOffset, toOffset)).ToArray();

    private static LedgerOffset? OffsetOf<TInterface, TView>(InterfaceAcsSnapshotEntry<TInterface, TView> entry)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> => entry switch
    {
        InterfaceAcsSnapshotEntry<TInterface, TView>.Created created => created.Offset,
        InterfaceAcsSnapshotEntry<TInterface, TView>.Unclassified unclassified => unclassified.Offset,
        _ => null,
    };

    private static LedgerOffset? OffsetOf<TInterface, TView>(InterfaceStreamEvent<TInterface, TView> streamEvent)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> => streamEvent switch
    {
        InterfaceStreamEvent<TInterface, TView>.Created created => created.Offset,
        InterfaceStreamEvent<TInterface, TView>.Archived archived => archived.Offset,
        InterfaceStreamEvent<TInterface, TView>.Exercised exercised => exercised.Offset,
        InterfaceStreamEvent<TInterface, TView>.Assigned assigned => assigned.Offset,
        InterfaceStreamEvent<TInterface, TView>.Unassigned unassigned => unassigned.Offset,
        InterfaceStreamEvent<TInterface, TView>.Unclassified unclassified => unclassified.Offset,
        InterfaceStreamEvent<TInterface, TView>.Checkpoint checkpoint => checkpoint.Offset,
        _ => null,
    };
}

/// <summary>
/// The interface-family reads a <see cref="FakeLedgerClient"/> replays, keyed by interface marker
/// type — the counterpart of the template-family registries the client holds directly.
/// </summary>
/// <param name="ActiveContracts">Snapshots staged per interface marker.</param>
/// <param name="StreamEvents">ACS-delta streams staged per interface marker.</param>
/// <param name="LedgerEffects">Ledger-effects streams staged per interface marker.</param>
internal sealed record FakeInterfaceStreams(
    IReadOnlyDictionary<Type, object> ActiveContracts,
    IReadOnlyDictionary<Type, object> StreamEvents,
    IReadOnlyDictionary<Type, object> LedgerEffects);
