// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Testing;

/// <summary>
/// Static factories for the <see cref="ContractStreamEvent{T}"/> variants a contract stream
/// (ACS-delta or ledger-effects shape) yields. Each thinly wraps the corresponding public record
/// constructor. Pair these with <see cref="FakeLedgerClientBuilder.WithContractEvents{T}"/> or
/// <see cref="FakeLedgerClientBuilder.WithLedgerEffects{T}"/>.
/// </summary>
public static class ContractEvents
{
    /// <summary>Builds a <see cref="ContractStreamEvent{T}.Created"/> event.</summary>
    /// <typeparam name="T">The Daml template or interface marker the contract is projected as.</typeparam>
    /// <returns>The created event.</returns>
    public static ContractStreamEvent<T> Created<T>(
        ContractId<T> contractId,
        DamlRecord payload,
        LedgerOffset offset,
        SynchronizerId synchronizerId,
        IReadOnlyList<Party> witnessParties)
        where T : IDamlType =>
        new ContractStreamEvent<T>.Created(contractId, payload, offset, synchronizerId, witnessParties);

    /// <summary>Builds an <see cref="ContractStreamEvent{T}.Archived"/> event (ACS-delta shape).</summary>
    /// <typeparam name="T">The Daml template or interface marker the contract is projected as.</typeparam>
    /// <returns>The archived event.</returns>
    public static ContractStreamEvent<T> Archived<T>(
        ContractId<T> contractId,
        LedgerOffset offset,
        SynchronizerId synchronizerId,
        IReadOnlyList<Party> witnessParties)
        where T : IDamlType =>
        new ContractStreamEvent<T>.Archived(contractId, offset, synchronizerId, witnessParties);

    /// <summary>Builds an <see cref="ContractStreamEvent{T}.Exercised"/> event (ledger-effects shape).</summary>
    /// <typeparam name="T">The Daml template or interface marker the contract is projected as.</typeparam>
    /// <returns>The exercised event.</returns>
    public static ContractStreamEvent<T> Exercised<T>(
        ContractId<T> contractId,
        string choiceName,
        DamlValue choiceArgument,
        DamlValue exerciseResult,
        bool consuming,
        LedgerOffset offset,
        SynchronizerId synchronizerId,
        IReadOnlyList<Party> witnessParties)
        where T : IDamlType =>
        new ContractStreamEvent<T>.Exercised(
            contractId, choiceName, choiceArgument, exerciseResult, consuming, offset, synchronizerId, witnessParties);

    /// <summary>Builds an <see cref="ContractStreamEvent{T}.Assigned"/> reassignment event.</summary>
    /// <typeparam name="T">The Daml template or interface marker the contract is projected as.</typeparam>
    /// <returns>The assigned event.</returns>
    public static ContractStreamEvent<T> Assigned<T>(
        ContractId<T> contractId,
        DamlRecord payload,
        LedgerOffset offset,
        SynchronizerId source,
        SynchronizerId target,
        string reassignmentId,
        long reassignmentCounter,
        IReadOnlyList<Party> witnessParties)
        where T : IDamlType =>
        new ContractStreamEvent<T>.Assigned(
            contractId, payload, offset, source, target, reassignmentId, reassignmentCounter, witnessParties);

    /// <summary>Builds an <see cref="ContractStreamEvent{T}.Unassigned"/> reassignment event.</summary>
    /// <typeparam name="T">The Daml template or interface marker the contract is projected as.</typeparam>
    /// <returns>The unassigned event.</returns>
    public static ContractStreamEvent<T> Unassigned<T>(
        ContractId<T> contractId,
        LedgerOffset offset,
        SynchronizerId source,
        SynchronizerId target,
        string reassignmentId,
        long reassignmentCounter,
        IReadOnlyList<Party> witnessParties)
        where T : IDamlType =>
        new ContractStreamEvent<T>.Unassigned(
            contractId, offset, source, target, reassignmentId, reassignmentCounter, witnessParties);

    /// <summary>Builds a <see cref="ContractStreamEvent{T}.Checkpoint"/> event.</summary>
    /// <typeparam name="T">The Daml template or interface marker the stream is for.</typeparam>
    /// <returns>The checkpoint event.</returns>
    public static ContractStreamEvent<T> Checkpoint<T>(LedgerOffset offset)
        where T : IDamlType =>
        new ContractStreamEvent<T>.Checkpoint(offset);

    /// <summary>Builds an in-band <see cref="ContractStreamEvent{T}.StreamError"/> event.</summary>
    /// <typeparam name="T">The Daml template or interface marker the stream is for.</typeparam>
    /// <returns>The stream-error event.</returns>
    public static ContractStreamEvent<T> StreamError<T>(int statusCode, string message)
        where T : IDamlType =>
        new ContractStreamEvent<T>.StreamError(statusCode, message);

    /// <summary>Builds an <see cref="ContractStreamEvent{T}.Unclassified"/> event.</summary>
    /// <typeparam name="T">The Daml template or interface marker the stream is for.</typeparam>
    /// <returns>The unclassified event.</returns>
    public static ContractStreamEvent<T> Unclassified<T>(LedgerOffset offset, UnclassifiedKind kind, string? rawKind = null)
        where T : IDamlType =>
        new ContractStreamEvent<T>.Unclassified(offset, kind, rawKind);
}
