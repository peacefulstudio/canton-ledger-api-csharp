// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Abstractions;

internal static class InterfaceViewSnapshot
{
    public static async Task<IReadOnlyList<InterfaceContract<TInterface, TView>>> DrainAsync<TInterface, TView>(
        IAsyncEnumerable<AcsSnapshotEntry<TInterface>> snapshot,
        CancellationToken cancellationToken)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var contracts = new List<InterfaceContract<TInterface, TView>>();
        var reachedCheckpoint = false;

        await foreach (var entry in snapshot.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (entry is AcsSnapshotEntry<TInterface>.Created created)
            {
                contracts.Add(new InterfaceContract<TInterface, TView>(created.ContractId, Decode<TInterface, TView>(created)));
                continue;
            }

            if (entry is AcsSnapshotEntry<TInterface>.Checkpoint)
            {
                reachedCheckpoint = true;
                break;
            }

            if (entry is AcsSnapshotEntry<TInterface>.StreamError error)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new LedgerOperationException(
                    $"The active-contract-set snapshot for {typeof(TInterface).Name} faulted after {contracts.Count} "
                    + $"interface view(s): {error.Message}. Use SubscribeActiveAsync<{typeof(TInterface).Name}> for "
                    + "value-shaped fault handling.",
                    error.StatusCode);
            }

            if (entry is AcsSnapshotEntry<TInterface>.Unclassified unclassified)
            {
                throw new LedgerOperationException(
                    $"The active-contract-set snapshot for {typeof(TInterface).Name} carried an unclassified row "
                    + $"({unclassified.Kind}) at offset {unclassified.Offset.Value}, so the returned views would be "
                    + $"incomplete. Use SubscribeActiveAsync<{typeof(TInterface).Name}> to handle it as a value.");
            }

            throw new LedgerOperationException(
                $"Unexpected snapshot entry {entry.GetType().Name} for {typeof(TInterface).Name}.");
        }

        if (!reachedCheckpoint)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new LedgerOperationException(
                $"The active-contract-set snapshot for {typeof(TInterface).Name} ended after {contracts.Count} "
                + "interface view(s) without its terminal checkpoint, so the returned views would be incomplete.");
        }

        return contracts;
    }

    private static TView Decode<TInterface, TView>(AcsSnapshotEntry<TInterface>.Created created)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord
    {
        try
        {
            return InterfaceViewDecoder<TView>.FromRecord(created.Payload);
        }
        catch (Exception cause) when (cause is not OperationCanceledException and not LedgerOperationException)
        {
            throw Undecodable<TInterface, TView>(created, cause);
        }
    }

    private static LedgerOperationException Undecodable<TInterface, TView>(
        AcsSnapshotEntry<TInterface>.Created created,
        Exception cause)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord =>
        new($"The active-contract-set snapshot for {typeof(TInterface).Name} carried a row at offset "
            + $"{created.Offset.Value} whose interface view did not decode into {typeof(TView).Name}: {cause.Message}",
            cause);
}
