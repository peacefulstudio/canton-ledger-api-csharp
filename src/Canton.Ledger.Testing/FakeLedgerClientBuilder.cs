// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Testing;

/// <summary>
/// Fluent builder that stages the stream reads and command outcomes a
/// <see cref="FakeLedgerClient"/> replays, keyed by Daml type. Obtain one from
/// <see cref="FakeLedgerClient.Create"/>, chain <c>With*</c> calls, then call
/// <see cref="Build"/>.
/// </summary>
public sealed class FakeLedgerClientBuilder
{
    private readonly Dictionary<Type, object> _activeContracts = [];
    private readonly Dictionary<Type, object> _contractEvents = [];
    private readonly Dictionary<Type, object> _ledgerEffects = [];
    private readonly Dictionary<Type, object> _exerciseResults = [];
    private readonly Dictionary<Type, object> _createResults = [];
    private readonly Dictionary<Type, object> _reassignmentResults = [];
    private readonly Dictionary<long, TransactionResult> _updatesByOffset = [];
    private readonly Dictionary<string, TransactionResult> _updatesById = [];
    private LedgerOffset? _ledgerEnd;
    private IReadOnlyList<CompletionStreamEvent>? _completionEvents;
    private IReadOnlyList<ConnectedSynchronizer>? _connectedSynchronizers;
    private string? _ledgerApiVersion;
    private ExerciseOutcome<TransactionResult>? _submissionOutcome;
    private ExerciseOutcome<TransactionTree>? _transactionTreeOutcome;
    private StagedTrafficCostEstimate? _trafficCostEstimate;

    /// <summary>
    /// Stages the offset <see cref="FakeLedgerClient.GetLedgerEndAsync"/> starts from. The end
    /// advances by one offset per committed write, so an event a caller expects to find in a
    /// bounded window opened around the <c>n</c>th write belongs at <paramref name="offset"/>
    /// plus <c>n</c>.
    /// </summary>
    /// <param name="offset">The ledger end offset before any write.</param>
    /// <returns>The same builder, for chaining.</returns>
    public FakeLedgerClientBuilder WithLedgerEnd(LedgerOffset offset)
    {
        _ledgerEnd = offset;
        return this;
    }

    /// <summary>
    /// Stages the active-contract-set snapshot entries that
    /// <see cref="FakeLedgerClient.SubscribeActiveAsync{T}"/> replays for <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The Daml template or interface marker the snapshot is for.</typeparam>
    /// <returns>The same builder, for chaining.</returns>
    public FakeLedgerClientBuilder WithActiveContracts<T>(params AcsSnapshotEntry<T>[] events)
        where T : IDamlType
    {
        ArgumentNullException.ThrowIfNull(events);
        _activeContracts[typeof(T)] = events.ToArray();
        return this;
    }

    /// <summary>
    /// Stages the contract stream events that <see cref="FakeLedgerClient.SubscribeAsync{T}"/>
    /// (the ACS-delta shape) replays for <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The Daml template or interface marker the stream is for.</typeparam>
    /// <returns>The same builder, for chaining.</returns>
    public FakeLedgerClientBuilder WithContractEvents<T>(params ContractStreamEvent<T>[] events)
        where T : IDamlType
    {
        ArgumentNullException.ThrowIfNull(events);
        _contractEvents[typeof(T)] = events.ToArray();
        return this;
    }

    /// <summary>
    /// Stages the contract stream events that
    /// <see cref="FakeLedgerClient.SubscribeLedgerEffectsAsync{T}"/> (the ledger-effects shape)
    /// replays for <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The Daml template or interface marker the stream is for.</typeparam>
    /// <returns>The same builder, for chaining.</returns>
    public FakeLedgerClientBuilder WithLedgerEffects<T>(params ContractStreamEvent<T>[] events)
        where T : IDamlType
    {
        ArgumentNullException.ThrowIfNull(events);
        _ledgerEffects[typeof(T)] = events.ToArray();
        return this;
    }

    /// <summary>
    /// Stages the outcome that <see cref="FakeLedgerClient.TryExerciseAsync{TResult}"/> returns
    /// for choice result type <typeparamref name="TResult"/>.
    /// </summary>
    /// <typeparam name="TResult">The choice result type the outcome is for.</typeparam>
    /// <returns>The same builder, for chaining.</returns>
    public FakeLedgerClientBuilder WithExerciseResult<TResult>(ExerciseOutcome<TResult> outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        _exerciseResults[typeof(TResult)] = outcome;
        return this;
    }

    /// <summary>
    /// Stages the outcome that <see cref="FakeLedgerClient.TryCreateAsync{TTemplate}"/> returns
    /// for template type <typeparamref name="TTemplate"/>.
    /// </summary>
    /// <typeparam name="TTemplate">The template type the create outcome is for.</typeparam>
    /// <returns>The same builder, for chaining.</returns>
    public FakeLedgerClientBuilder WithCreateResult<TTemplate>(ExerciseOutcome<ContractId<TTemplate>> outcome)
        where TTemplate : ITemplate
    {
        ArgumentNullException.ThrowIfNull(outcome);
        _createResults[typeof(TTemplate)] = outcome;
        return this;
    }

    /// <summary>
    /// Stages the outcome that
    /// <see cref="FakeLedgerClient.TrySubmitAndWaitForTransactionAsync(CommandsSubmission, TimeSpan?, CancellationToken)"/>
    /// (and its <see cref="SubmitterInfo"/>-carrying overload) returns for every submission.
    /// </summary>
    /// <param name="outcome">The outcome to reply with.</param>
    /// <returns>The same builder, for chaining.</returns>
    public FakeLedgerClientBuilder WithSubmissionOutcome(ExerciseOutcome<TransactionResult> outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        _submissionOutcome = outcome;
        return this;
    }

    /// <summary>
    /// Stages the outcome that
    /// <see cref="FakeLedgerClient.TrySubmitAndWaitForTransactionTreeAsync"/> returns for every
    /// submission.
    /// </summary>
    /// <param name="outcome">The tree-shaped outcome to reply with.</param>
    /// <returns>The same builder, for chaining.</returns>
    public FakeLedgerClientBuilder WithTransactionTree(ExerciseOutcome<TransactionTree> outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        _transactionTreeOutcome = outcome;
        return this;
    }

    /// <summary>
    /// Stages the outcome that <see cref="FakeLedgerClient.TrySubmitAndWaitForReassignmentAsync{T}"/>
    /// returns for projected reassignment type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The template or interface marker the reassignment is projected as.</typeparam>
    /// <returns>The same builder, for chaining.</returns>
    public FakeLedgerClientBuilder WithReassignmentResult<T>(ExerciseOutcome<ContractStreamEvent<T>> outcome)
        where T : IDamlType
    {
        ArgumentNullException.ThrowIfNull(outcome);
        _reassignmentResults[typeof(T)] = outcome;
        return this;
    }

    /// <summary>
    /// Stages the completion-stream events that <see cref="FakeLedgerClient.CompletionStreamAsync"/>
    /// replays, in order. Staging an empty sequence drains to nothing; leaving completions unstaged
    /// makes <see cref="FakeLedgerClient.CompletionStreamAsync"/> throw.
    /// </summary>
    /// <param name="events">The completion events to replay.</param>
    /// <returns>The same builder, for chaining.</returns>
    public FakeLedgerClientBuilder WithCompletionEvents(params CompletionStreamEvent[] events)
    {
        ArgumentNullException.ThrowIfNull(events);
        _completionEvents = events.ToArray();
        return this;
    }

    /// <summary>
    /// Stages the synchronizers that
    /// <see cref="FakeLedgerClient.GetConnectedSynchronizersAsync"/> returns.
    /// </summary>
    /// <param name="synchronizers">The connected synchronizers to reply with.</param>
    /// <returns>The same builder, for chaining.</returns>
    public FakeLedgerClientBuilder WithConnectedSynchronizers(params ConnectedSynchronizer[] synchronizers)
    {
        ArgumentNullException.ThrowIfNull(synchronizers);
        _connectedSynchronizers = synchronizers.ToArray();
        return this;
    }

    /// <summary>
    /// Stages the version that <see cref="FakeLedgerClient.GetLedgerApiVersionAsync"/> returns.
    /// </summary>
    /// <param name="version">The Ledger API version string to reply with.</param>
    /// <returns>The same builder, for chaining.</returns>
    public FakeLedgerClientBuilder WithLedgerApiVersion(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        _ledgerApiVersion = version;
        return this;
    }

    /// <summary>
    /// Stages the transaction that <see cref="FakeLedgerClient.GetUpdateByOffsetAsync"/> returns for
    /// <paramref name="offset"/>.
    /// </summary>
    /// <param name="offset">The absolute offset the point read looks up.</param>
    /// <param name="transaction">The transaction to reply with.</param>
    /// <returns>The same builder, for chaining.</returns>
    public FakeLedgerClientBuilder WithUpdateByOffset(long offset, TransactionResult transaction)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(offset);
        ArgumentNullException.ThrowIfNull(transaction);
        _updatesByOffset[offset] = transaction;
        return this;
    }

    /// <summary>
    /// Stages the transaction that <see cref="FakeLedgerClient.GetUpdateByIdAsync"/> returns for
    /// <paramref name="updateId"/>.
    /// </summary>
    /// <param name="updateId">The update id the point read looks up.</param>
    /// <param name="transaction">The transaction to reply with.</param>
    /// <returns>The same builder, for chaining.</returns>
    public FakeLedgerClientBuilder WithUpdateById(string updateId, TransactionResult transaction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(updateId);
        ArgumentNullException.ThrowIfNull(transaction);
        _updatesById[updateId] = transaction;
        return this;
    }

    /// <summary>
    /// Stages the answer that <see cref="FakeLedgerClient.EstimateTrafficCostAsync"/> returns for
    /// every submission. Staging <see langword="null"/> replays a participant that served no
    /// estimation — the distinct case from a present estimation reporting zero — while leaving the
    /// estimate unstaged makes <see cref="FakeLedgerClient.EstimateTrafficCostAsync"/> throw.
    /// </summary>
    /// <param name="estimate">The estimate to reply with, or <see langword="null"/> for none.</param>
    /// <returns>The same builder, for chaining.</returns>
    public FakeLedgerClientBuilder WithTrafficCostEstimate(TrafficCostEstimate? estimate)
    {
        _trafficCostEstimate = new StagedTrafficCostEstimate(estimate);
        return this;
    }

    /// <summary>Builds a <see cref="FakeLedgerClient"/> from the currently staged behaviour.</summary>
    /// <returns>
    /// A fake whose behaviour is a snapshot of this builder; later mutation of the builder does
    /// not affect an already-built client.
    /// </returns>
    public FakeLedgerClient Build() => new(
        new Dictionary<Type, object>(_activeContracts),
        new Dictionary<Type, object>(_contractEvents),
        new Dictionary<Type, object>(_ledgerEffects),
        new Dictionary<Type, object>(_exerciseResults),
        new Dictionary<Type, object>(_createResults),
        _submissionOutcome,
        _ledgerEnd,
        new FakeCantonSurface(
            new Dictionary<Type, object>(_reassignmentResults),
            _transactionTreeOutcome,
            _completionEvents,
            _connectedSynchronizers,
            _ledgerApiVersion,
            _trafficCostEstimate,
            new Dictionary<long, TransactionResult>(_updatesByOffset),
            new Dictionary<string, TransactionResult>(_updatesById)));
}
