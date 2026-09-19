// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using RuntimeCommands = Daml.Runtime.Commands;
using WireGetActiveContractsResponse = Canton.Ledger.Rest.Client.Raw.GetActiveContractsResponse;
using WireGetUpdatesResponse = Canton.Ledger.Rest.Client.Raw.GetUpdatesResponse;

namespace Canton.Ledger.Rest.Client;

internal sealed partial class RestLedgerClient
{
    /// <inheritdoc />
    /// <remarks>
    /// The interface-family counterpart of <see cref="SubscribeActiveAsync{T}"/>: one window of
    /// <c>POST /v2/state/active-contracts</c> whose <c>InterfaceFilter</c> for
    /// <typeparamref name="TInterface"/> sets <c>includeInterfaceView</c>, so each row carries the
    /// participant-computed view decoded into <typeparamref name="TView"/> rather than the
    /// implementing template's create argument. A row delivered without a usable view arrives as
    /// <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}.Unclassified"/> with
    /// <see cref="UnclassifiedKind.InterfaceViewUnavailable"/>. The snapshot ends with a terminal
    /// <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}.Checkpoint"/> even when empty, or
    /// with a terminal <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}.StreamError"/> — a
    /// 413 among them — when the window failed, never both.
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

        return SubscribeActiveInterfaceAsyncCore<TInterface, TView>(submitter, activeAtOffset, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The interface-family counterpart of <see cref="SubscribeAsync{T}"/>: one bounded, blocking
    /// <c>POST /v2/updates</c> in the ACS-delta shape carrying the same <c>InterfaceFilter</c> and
    /// view-decoding contract as
    /// <see cref="SubscribeActiveAsync{TInterface, TView}(ViewDescriptor{TInterface, TView}, RuntimeCommands.SubmitterInfo, LedgerOffset?, CancellationToken)"/>.
    /// A <paramref name="toOffset"/> of <see langword="null"/> is an open-ended tail the loop
    /// follows; a value terminates it. An already-cancelled <paramref name="cancellationToken"/> is
    /// honored before any request is sent, and a failed window ends the enumeration with a terminal
    /// <see cref="InterfaceStreamEvent{TInterface, TView}.StreamError"/>.
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
        cancellationToken.ThrowIfCancellationRequested();

        return SubscribeInterfaceUpdatesAsyncCore<TInterface, TView>(
            submitter, fromOffset, toOffset, RestTransactionShape.AcsDelta, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The interface-family counterpart of <see cref="SubscribeLedgerEffectsAsync{T}"/>, identical
    /// to
    /// <see cref="SubscribeAsync{TInterface, TView}(ViewDescriptor{TInterface, TView}, RuntimeCommands.SubmitterInfo, LedgerOffset?, LedgerOffset?, CancellationToken)"/>
    /// but for the ledger-effects transaction shape.
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
        cancellationToken.ThrowIfCancellationRequested();

        return SubscribeInterfaceUpdatesAsyncCore<TInterface, TView>(
            submitter, fromOffset, toOffset, RestTransactionShape.LedgerEffects, cancellationToken);
    }

    private async IAsyncEnumerable<InterfaceAcsSnapshotEntry<TInterface, TView>> SubscribeActiveInterfaceAsyncCore<TInterface, TView>(
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? activeAtOffset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        var effectiveOffset = activeAtOffset
            ?? await GetLedgerEndAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var request = RestSubscribeRequestBuilder.BuildGetActiveContractsRequest<TInterface>(
            submitter, effectiveOffset.Value);

        var window = await ReadWindowAsync<WireGetActiveContractsResponse>(
            ActiveContractsPath, request, cancellationToken).ConfigureAwait(false);
        if (window.Fault is { } fault)
        {
            yield return new InterfaceAcsSnapshotEntry<TInterface, TView>.StreamError(
                fault.StatusCode, fault.Message, fault.Category, fault.ErrorId, fault.SourceException);
            yield break;
        }

        foreach (var entry in window.Entries)
        {
            foreach (var projected in InterfaceStreamProjector.ProjectActiveContractEntry<TInterface, TView>(
                entry, _logger, effectiveOffset))
            {
                yield return ToInterfaceAcsSnapshotEntry<TInterface, TView>(projected);
            }
        }

        yield return new InterfaceAcsSnapshotEntry<TInterface, TView>.Checkpoint(new StakeholderResume(effectiveOffset));
    }

    private async IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeInterfaceUpdatesAsyncCore<TInterface, TView>(
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? fromOffset,
        LedgerOffset? toOffset,
        RestTransactionShape shape,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        var windows = ReadUpdateWindowsAsync<TInterface>(submitter, fromOffset, toOffset, shape, cancellationToken);
        await foreach (var read in windows.ConfigureAwait(false))
        {
            if (read.Fault is { } fault)
            {
                yield return new InterfaceStreamEvent<TInterface, TView>.StreamError(
                    fault.StatusCode, fault.Message, fault.Category, fault.ErrorId, fault.SourceException);
                yield break;
            }

            if (read.Entry is not { } update)
            {
                continue;
            }

            foreach (var projected in ProjectInterfaceUpdate<TInterface, TView>(update))
            {
                yield return projected;
            }
        }
    }

    private IEnumerable<InterfaceStreamEvent<TInterface, TView>> ProjectInterfaceUpdate<TInterface, TView>(
        WireGetUpdatesResponse update)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        if (update.Update?.Transaction is { } transaction)
        {
            foreach (var projected in InterfaceStreamProjector.ProjectTransactionEvents<TInterface, TView>(
                transaction, _logger))
            {
                yield return projected;
            }
        }
        else if (update.Update?.Reassignment is { } reassignment)
        {
            foreach (var projected in InterfaceStreamProjector.ProjectReassignmentEvents<TInterface, TView>(
                reassignment, _logger))
            {
                yield return projected;
            }
        }
        else if (update.Update?.OffsetCheckpoint is { } checkpoint)
        {
            yield return new InterfaceStreamEvent<TInterface, TView>.Checkpoint(
                LedgerOffset.At(RestWireConversions.ParseOffset(checkpoint.Offset)));
        }
        else
        {
            var variant = update.Update?.TopologyTransaction is not null ? nameof(update.Update.TopologyTransaction) : "Unknown";
            LogStreamVariantSkipped(_logger, typeof(TInterface).Name, variant);
        }
    }

    private static InterfaceAcsSnapshotEntry<TInterface, TView> ToInterfaceAcsSnapshotEntry<TInterface, TView>(
        InterfaceStreamEvent<TInterface, TView> entry)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> => entry switch
    {
        InterfaceStreamEvent<TInterface, TView>.Created created =>
            new InterfaceAcsSnapshotEntry<TInterface, TView>.Created(
                created.ContractId,
                created.Payload,
                created.Key,
                created.Offset,
                created.SynchronizerId,
                created.WitnessParties),
        InterfaceStreamEvent<TInterface, TView>.Unassigned unassigned =>
            new InterfaceAcsSnapshotEntry<TInterface, TView>.Unclassified(
                unassigned.Offset, UnclassifiedKind.UnassignedEvent),
        InterfaceStreamEvent<TInterface, TView>.Unclassified unclassified =>
            new InterfaceAcsSnapshotEntry<TInterface, TView>.Unclassified(
                unclassified.Offset, unclassified.Kind, unclassified.RawKind),
        _ => throw new InvalidOperationException(
            $"Active-contract snapshot produced an unexpected entry variant: {entry.GetType().Name}"),
    };
}
