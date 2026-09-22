// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
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
        T payload,
        ContractKey? key,
        LedgerOffset offset,
        SynchronizerId synchronizerId,
        EquatableArray<Party> witnessParties)
        where T : ITemplate, IDamlRecord<T> =>
        new ContractStreamEvent<T>.Created(contractId, payload, key, offset, synchronizerId, witnessParties);

    /// <summary>Builds an <see cref="ContractStreamEvent{T}.Archived"/> event (ACS-delta shape).</summary>
    /// <typeparam name="T">The Daml template or interface marker the contract is projected as.</typeparam>
    /// <returns>The archived event.</returns>
    public static ContractStreamEvent<T> Archived<T>(
        ContractId<T> contractId,
        LedgerOffset offset,
        SynchronizerId synchronizerId,
        EquatableArray<Party> witnessParties)
        where T : ITemplate, IDamlRecord<T> =>
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
        EquatableArray<Party> witnessParties)
        where T : ITemplate, IDamlRecord<T> =>
        new ContractStreamEvent<T>.Exercised(
            contractId, choiceName, choiceArgument, exerciseResult, consuming, offset, synchronizerId, witnessParties);

    /// <summary>Builds an <see cref="ContractStreamEvent{T}.Assigned"/> reassignment event.</summary>
    /// <typeparam name="T">The Daml template or interface marker the contract is projected as.</typeparam>
    /// <returns>The assigned event.</returns>
    public static ContractStreamEvent<T> Assigned<T>(
        ContractId<T> contractId,
        T payload,
        ContractKey? key,
        LedgerOffset offset,
        SynchronizerId source,
        SynchronizerId target,
        string reassignmentId,
        long reassignmentCounter,
        EquatableArray<Party> witnessParties)
        where T : ITemplate, IDamlRecord<T> =>
        new ContractStreamEvent<T>.Assigned(
            contractId, payload, key, offset, source, target, reassignmentId, reassignmentCounter, witnessParties);

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
        EquatableArray<Party> witnessParties)
        where T : ITemplate, IDamlRecord<T> =>
        new ContractStreamEvent<T>.Unassigned(
            contractId, offset, source, target, reassignmentId, reassignmentCounter, witnessParties);

    /// <summary>Builds a <see cref="ContractStreamEvent{T}.Checkpoint"/> event.</summary>
    /// <typeparam name="T">The Daml template or interface marker the stream is for.</typeparam>
    /// <returns>The checkpoint event.</returns>
    public static ContractStreamEvent<T> Checkpoint<T>(LedgerOffset offset)
        where T : ITemplate, IDamlRecord<T> =>
        new ContractStreamEvent<T>.Checkpoint(offset);

    /// <summary>Builds an in-band <see cref="ContractStreamEvent{T}.StreamError"/> event.</summary>
    /// <typeparam name="T">The Daml template or interface marker the stream is for.</typeparam>
    /// <param name="statusCode">Transport-native status code the fault carried.</param>
    /// <param name="message">Status detail from the participant or transport.</param>
    /// <param name="category">Classification of the transport failure, or <c>null</c> to stage a
    /// fault the transport could not classify.</param>
    /// <param name="errorId">The participant's own Canton error code, or <c>null</c> to stage a
    /// fault that carried no structured error to read one from.</param>
    /// <param name="sourceException">Transport exception that ended the stream, or <c>null</c> to
    /// stage a fault carried in-band rather than thrown.</param>
    /// <returns>The stream-error event.</returns>
    public static ContractStreamEvent<T> StreamError<T>(
        int statusCode,
        string message,
        DamlErrorCategory? category = null,
        string? errorId = null,
        Exception? sourceException = null)
        where T : ITemplate, IDamlRecord<T> =>
        new ContractStreamEvent<T>.StreamError(statusCode, message, category, errorId, sourceException);

    /// <summary>Builds an <see cref="ContractStreamEvent{T}.Unclassified"/> event.</summary>
    /// <typeparam name="T">The Daml template or interface marker the stream is for.</typeparam>
    /// <returns>The unclassified event.</returns>
    public static ContractStreamEvent<T> Unclassified<T>(LedgerOffset offset, UnclassifiedKind kind, string? rawKind = null)
        where T : ITemplate, IDamlRecord<T> =>
        new ContractStreamEvent<T>.Unclassified(offset, kind, rawKind);
}
