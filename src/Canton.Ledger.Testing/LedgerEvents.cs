// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
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
        T payload,
        ContractKey? key,
        LedgerOffset offset,
        SynchronizerId synchronizerId,
        EquatableArray<Party> witnessParties)
        where T : ITemplate, IDamlRecord<T> =>
        new AcsSnapshotEntry<T>.Created(contractId, payload, key, offset, synchronizerId, witnessParties);

    /// <summary>Builds the terminal <see cref="AcsSnapshotEntry{T}.Checkpoint"/> snapshot entry.</summary>
    /// <typeparam name="T">The Daml template or interface marker the snapshot is for.</typeparam>
    /// <returns>The checkpoint snapshot entry.</returns>
    public static AcsSnapshotEntry<T> Checkpoint<T>(LedgerOffset offset)
        where T : ITemplate, IDamlRecord<T> =>
        new AcsSnapshotEntry<T>.Checkpoint(new StakeholderResume(offset));

    /// <summary>Builds an in-band <see cref="AcsSnapshotEntry{T}.StreamError"/> snapshot entry.</summary>
    /// <typeparam name="T">The Daml template or interface marker the snapshot is for.</typeparam>
    /// <param name="statusCode">Transport-native status code the fault carried.</param>
    /// <param name="message">Status detail from the participant or transport.</param>
    /// <param name="category">Classification of the transport failure, or <c>null</c> to stage a
    /// fault the transport could not classify.</param>
    /// <param name="errorId">The participant's own Canton error code, or <c>null</c> to stage a
    /// fault that carried no structured error to read one from.</param>
    /// <param name="sourceException">Transport exception that ended the stream, or <c>null</c> to
    /// stage a fault carried in-band rather than thrown.</param>
    /// <returns>The stream-error snapshot entry.</returns>
    public static AcsSnapshotEntry<T> StreamError<T>(
        int statusCode,
        string message,
        DamlErrorCategory? category = null,
        string? errorId = null,
        Exception? sourceException = null)
        where T : ITemplate, IDamlRecord<T> =>
        new AcsSnapshotEntry<T>.StreamError(statusCode, message, category, errorId, sourceException);

    /// <summary>Builds an <see cref="AcsSnapshotEntry{T}.Unclassified"/> snapshot entry.</summary>
    /// <typeparam name="T">The Daml template or interface marker the snapshot is for.</typeparam>
    /// <returns>The unclassified snapshot entry.</returns>
    public static AcsSnapshotEntry<T> Unclassified<T>(
        LedgerOffset? offset,
        UnclassifiedKind kind,
        string? rawKind = null)
        where T : ITemplate, IDamlRecord<T> =>
        new AcsSnapshotEntry<T>.Unclassified(offset, kind, rawKind);
}
