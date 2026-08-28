// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Testing;

/// <summary>
/// Static factories for the <see cref="AcsSnapshotEntry{T}"/> variants an active-contract-set
/// snapshot stream yields. Each thinly wraps the corresponding public record constructor so a
/// test can stage snapshot entries without hand-writing the constructor calls. Pair these with
/// <see cref="FakeLedgerClientBuilder.WithActiveContracts{T}"/>.
/// </summary>
public static class LedgerEvents
{
    /// <summary>Builds an <see cref="AcsSnapshotEntry{T}.Created"/> snapshot entry.</summary>
    /// <typeparam name="T">The Daml template or interface marker the contract is projected as.</typeparam>
    /// <returns>The created snapshot entry.</returns>
    public static AcsSnapshotEntry<T> Created<T>(
        ContractId<T> contractId,
        DamlRecord payload,
        LedgerOffset offset,
        SynchronizerId synchronizerId,
        IReadOnlyList<Party> witnessParties)
        where T : IDamlType =>
        new AcsSnapshotEntry<T>.Created(contractId, payload, offset, synchronizerId, witnessParties);

    /// <summary>Builds the terminal <see cref="AcsSnapshotEntry{T}.Checkpoint"/> snapshot entry.</summary>
    /// <typeparam name="T">The Daml template or interface marker the snapshot is for.</typeparam>
    /// <returns>The checkpoint snapshot entry.</returns>
    public static AcsSnapshotEntry<T> Checkpoint<T>(LedgerOffset offset)
        where T : IDamlType =>
        new AcsSnapshotEntry<T>.Checkpoint(new StakeholderResume(offset));

    /// <summary>Builds an in-band <see cref="AcsSnapshotEntry{T}.StreamError"/> snapshot entry.</summary>
    /// <typeparam name="T">The Daml template or interface marker the snapshot is for.</typeparam>
    /// <returns>The stream-error snapshot entry.</returns>
    public static AcsSnapshotEntry<T> StreamError<T>(int statusCode, string message)
        where T : IDamlType =>
        new AcsSnapshotEntry<T>.StreamError(statusCode, message);

    /// <summary>Builds an <see cref="AcsSnapshotEntry{T}.Unclassified"/> snapshot entry.</summary>
    /// <typeparam name="T">The Daml template or interface marker the snapshot is for.</typeparam>
    /// <returns>The unclassified snapshot entry.</returns>
    public static AcsSnapshotEntry<T> Unclassified<T>(LedgerOffset offset, string kind)
        where T : IDamlType =>
        new AcsSnapshotEntry<T>.Unclassified(offset, kind);
}
