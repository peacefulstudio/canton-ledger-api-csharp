// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Testing;

/// <summary>
/// An in-memory <see cref="ICantonLedgerClient"/> test double — the full Canton participant
/// surface, so it also satisfies the neutral <see cref="ILedgerClient"/> — that replays canned
/// stream reads, command outcomes, and completion events staged through the fluent
/// <see cref="FakeLedgerClientBuilder"/>. It lets business logic that talks to the ledger
/// client be unit-tested without a live participant and without a mocking framework.
/// </summary>
/// <remarks>
/// The fake replays events; it is not a semantic ledger simulator (no contract-key
/// uniqueness or consuming-choice archival). Its one piece of ledger arithmetic is the ledger
/// end: it starts at the offset staged through <see cref="FakeLedgerClientBuilder.WithLedgerEnd"/>
/// and advances by one offset per committed write, so a caller can read a bounded
/// <c>(fromOffset, toOffset]</c> window around a write. Staged event offsets are left exactly as
/// the builder was given them. Any member or Daml type that was not staged throws a descriptive
/// <see cref="NotSupportedException"/> naming the missing setup, so a test never silently
/// exercises unconfigured behaviour. Construct instances through <see cref="Create"/>.
/// </remarks>
public sealed partial class FakeLedgerClient : ICantonLedgerClient
{
    private readonly IReadOnlyDictionary<Type, object> _activeContracts;
    private readonly IReadOnlyDictionary<Type, object> _contractEvents;
    private readonly IReadOnlyDictionary<Type, object> _ledgerEffects;
    private readonly IReadOnlyDictionary<Type, object> _exerciseResults;
    private readonly IReadOnlyDictionary<Type, object> _createResults;
    private readonly ExerciseOutcome<TransactionResult>? _submissionOutcome;
    private readonly LedgerOffset? _ledgerEnd;
    private readonly FakeCantonSurface _canton;
    private long _committedWrites;

    internal FakeLedgerClient(
        IReadOnlyDictionary<Type, object> activeContracts,
        IReadOnlyDictionary<Type, object> contractEvents,
        IReadOnlyDictionary<Type, object> ledgerEffects,
        IReadOnlyDictionary<Type, object> exerciseResults,
        IReadOnlyDictionary<Type, object> createResults,
        ExerciseOutcome<TransactionResult>? submissionOutcome,
        LedgerOffset? ledgerEnd,
        FakeCantonSurface canton)
    {
        _activeContracts = activeContracts;
        _contractEvents = contractEvents;
        _ledgerEffects = ledgerEffects;
        _exerciseResults = exerciseResults;
        _createResults = createResults;
        _submissionOutcome = submissionOutcome;
        _ledgerEnd = ledgerEnd;
        _canton = canton;
    }

    /// <summary>Starts a new fluent builder for a <see cref="FakeLedgerClient"/>.</summary>
    /// <returns>An empty builder; stage behaviour on it, then call <see cref="FakeLedgerClientBuilder.Build"/>.</returns>
    public static FakeLedgerClientBuilder Create() => new();

    /// <inheritdoc />
    public IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType =>
        Replay(
            ActiveAt(
                Staged<IReadOnlyList<AcsSnapshotEntry<T>>>(
                    _activeContracts, typeof(T), "active-contract snapshot", $"WithActiveContracts<{typeof(T).Name}>"),
                activeAtOffset),
            cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType =>
        Replay(
            Within(
                Staged<IReadOnlyList<ContractStreamEvent<T>>>(
                    _contractEvents, typeof(T), "contract stream", $"WithContractEvents<{typeof(T).Name}>"),
                fromOffset,
                toOffset),
            cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType =>
        Replay(
            Within(
                Staged<IReadOnlyList<ContractStreamEvent<T>>>(
                    _ledgerEffects, typeof(T), "ledger-effects stream", $"WithLedgerEffects<{typeof(T).Name}>"),
                fromOffset,
                toOffset),
            cancellationToken);

    /// <inheritdoc />
    public Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        ExerciseCommand command,
        SubmitterInfo submitter,
        string? workflowId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            AdvancingLedgerEndOnCommit(
                Staged<ExerciseOutcome<TResult>>(
                    _exerciseResults, typeof(TResult), "exercise outcome", $"WithExerciseResult<{typeof(TResult).Name}>")));

    /// <inheritdoc />
    public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload,
        SubmitterInfo submitter,
        string? workflowId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate =>
        Task.FromResult(
            AdvancingLedgerEndOnCommit(
                Staged<ExerciseOutcome<ContractId<TTemplate>>>(
                    _createResults, typeof(TTemplate), "create outcome", $"WithCreateResult<{typeof(TTemplate).Name}>")));

    /// <inheritdoc />
    public Task<SubmitAndWaitResult> SubmitAndWaitAsync(
        CommandsSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        throw Unsupported(nameof(SubmitAndWaitAsync));

    /// <inheritdoc />
    public Task<SubmitAndWaitResult> SubmitAndWaitAsync(
        CommandsSubmission submission,
        SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        SubmitAndWaitAsync(submission.WithSubmitter(submitter), timeout, cancellationToken);

    /// <inheritdoc />
    public Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
        CommandsSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            AdvancingLedgerEndOnCommit(_submissionOutcome ?? throw StagingMissing(
                "submission outcome", nameof(TrySubmitAndWaitForTransactionAsync), "WithSubmissionOutcome")));

    /// <inheritdoc />
    public Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
        CommandsSubmission submission,
        SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        TrySubmitAndWaitForTransactionAsync(submission.WithSubmitter(submitter), timeout, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Returns the staged seed advanced by one offset for every outcome-returning write
    /// (<see cref="TryCreateAsync{TTemplate}"/>, <see cref="TryExerciseAsync{TResult}"/>,
    /// <see cref="TrySubmitAndWaitForTransactionAsync(CommandsSubmission, TimeSpan?, CancellationToken)"/>,
    /// <see cref="TrySubmitAndWaitForReassignmentAsync{T}"/>) that committed since this client was
    /// built, so two reads either side of a committed write bracket that write. The fire-and-forget
    /// <see cref="SubmitAsync"/> and <see cref="SubmitReassignmentAsync"/> report no outcome and so
    /// never advance it.
    /// </remarks>
    public Task<LedgerOffset> GetLedgerEndAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_ledgerEnd is { } seeded
            ? LedgerOffset.At(seeded.Value + Interlocked.Read(ref _committedWrites))
            : throw new NotSupportedException(
                $"FakeLedgerClient has no ledger end staged for '{nameof(GetLedgerEndAsync)}'. Stage one with " +
                "FakeLedgerClient.Create().WithLedgerEnd(...).Build() before exercising this path."));

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private ExerciseOutcome<T> AdvancingLedgerEndOnCommit<T>(ExerciseOutcome<T> outcome)
    {
        if (outcome is not (ExerciseOutcome<T>.DamlError or ExerciseOutcome<T>.InfraError))
        {
            Interlocked.Increment(ref _committedWrites);
        }

        return outcome;
    }

    private static TStaged Staged<TStaged>(
        IReadOnlyDictionary<Type, object> registry,
        Type key,
        string what,
        string builderCall)
    {
        if (registry.TryGetValue(key, out var staged))
        {
            return (TStaged)staged;
        }

        throw new NotSupportedException(
            $"FakeLedgerClient has no {what} staged for Daml type '{key.Name}'. Stage one with " +
            $"FakeLedgerClient.Create().{builderCall}(...).Build() before exercising this path.");
    }

    private static NotSupportedException Unsupported(string member) =>
        new($"FakeLedgerClient does not implement '{member}'. This fake replays only the stream reads and command " +
            $"outcomes staged through FakeLedgerClient.Create(); '{member}' has no staged behaviour.");

    private static async IAsyncEnumerable<TItem> Replay<TItem>(
        IReadOnlyList<TItem> items,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static IReadOnlyList<AcsSnapshotEntry<T>> ActiveAt<T>(
        IReadOnlyList<AcsSnapshotEntry<T>> entries,
        LedgerOffset? activeAtOffset)
        where T : IDamlType =>
        activeAtOffset is not { } snapshotOffset
            ? entries
            : entries.Where(entry => OffsetOf(entry) is not { } created || created.Value <= snapshotOffset.Value).ToArray();

    private static IReadOnlyList<ContractStreamEvent<T>> Within<T>(
        IReadOnlyList<ContractStreamEvent<T>> events,
        LedgerOffset? fromOffset,
        LedgerOffset? toOffset)
        where T : IDamlType =>
        fromOffset is null && toOffset is null
            ? events
            : events.Where(streamEvent => IsWithin(OffsetOf(streamEvent), fromOffset, toOffset)).ToArray();

    private static bool IsWithin(LedgerOffset? offset, LedgerOffset? fromOffset, LedgerOffset? toOffset)
    {
        if (offset is not { } at)
        {
            return true;
        }

        return (fromOffset is not { } beginExclusive || at.Value > beginExclusive.Value)
            && (toOffset is not { } endInclusive || at.Value <= endInclusive.Value);
    }

    private static LedgerOffset? OffsetOf<T>(AcsSnapshotEntry<T> entry)
        where T : IDamlType => entry switch
    {
        AcsSnapshotEntry<T>.Created created => created.Offset,
        AcsSnapshotEntry<T>.Unclassified unclassified => unclassified.Offset,
        _ => null,
    };

    private static LedgerOffset? OffsetOf<T>(ContractStreamEvent<T> streamEvent)
        where T : IDamlType => streamEvent switch
    {
        ContractStreamEvent<T>.Created created => created.Offset,
        ContractStreamEvent<T>.Archived archived => archived.Offset,
        ContractStreamEvent<T>.Exercised exercised => exercised.Offset,
        ContractStreamEvent<T>.Assigned assigned => assigned.Offset,
        ContractStreamEvent<T>.Unassigned unassigned => unassigned.Offset,
        ContractStreamEvent<T>.Unclassified unclassified => unclassified.Offset,
        ContractStreamEvent<T>.Checkpoint checkpoint => checkpoint.Offset,
        _ => null,
    };
}
