// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Abstractions;

internal static class InterfaceViewSnapshot
{
    public static async Task<IReadOnlyList<ActiveContract<InterfaceContract<TInterface, TView>>>> DrainAsync<TInterface, TView>(
        IAsyncEnumerable<InterfaceAcsSnapshotEntry<TInterface, TView>> snapshot,
        CancellationToken cancellationToken)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var contracts = new List<ActiveContract<InterfaceContract<TInterface, TView>>>();
        var reachedCheckpoint = false;

        await foreach (var entry in snapshot.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (entry is InterfaceAcsSnapshotEntry<TInterface, TView>.Created created)
            {
                contracts.Add(
                    new ActiveContract<InterfaceContract<TInterface, TView>>(
                        new InterfaceContract<TInterface, TView>(created.ContractId, created.Payload)
                        {
                            Key = created.Key,
                        },
                        created.Offset,
                        created.SynchronizerId));
                continue;
            }

            if (entry is InterfaceAcsSnapshotEntry<TInterface, TView>.Checkpoint)
            {
                reachedCheckpoint = true;
                break;
            }

            if (entry is InterfaceAcsSnapshotEntry<TInterface, TView>.StreamError error)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new LedgerOperationException(
                    $"The active-contract-set snapshot for {typeof(TInterface).Name} faulted after {contracts.Count} "
                    + $"interface view(s): {error.Message}. Use {ValueShapedAlternative<TInterface>()} for "
                    + "value-shaped fault handling.",
                    error.StatusCode);
            }

            if (entry is InterfaceAcsSnapshotEntry<TInterface, TView>.Unclassified unclassified)
            {
                throw new LedgerOperationException(
                    $"The active-contract-set snapshot for {typeof(TInterface).Name} carried an unclassified row "
                    + $"({DescribeKind(unclassified)}) at {DescribeOffset(unclassified.Offset)}, so the returned "
                    + $"views would be incomplete. Use {ValueShapedAlternative<TInterface>()} to handle it as a "
                    + "value.");
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

    private static string ValueShapedAlternative<TInterface>() =>
        $"SubscribeActiveAsync({typeof(TInterface).Name}.View, ...)";

    private static string DescribeKind<TInterface, TView>(
        InterfaceAcsSnapshotEntry<TInterface, TView>.Unclassified unclassified)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        unclassified.RawKind is { Length: > 0 } rawKind
            ? $"{unclassified.Kind}: {rawKind}"
            : unclassified.Kind.ToString();

    private static string DescribeOffset(LedgerOffset? offset) =>
        offset is { } present ? $"offset {present.Value}" : "an unreported offset";
}
