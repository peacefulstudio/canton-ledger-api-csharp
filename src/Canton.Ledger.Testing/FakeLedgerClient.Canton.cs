// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Testing;

public sealed partial class FakeLedgerClient
{
    /// <inheritdoc />
    public Task<CommandId> SubmitAsync(
        CommandsSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        return Task.FromResult(EffectiveCommandId(submission.CommandId));
    }

    /// <inheritdoc />
    public Task<CommandId> SubmitReassignmentAsync(
        ReassignmentSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        return Task.FromResult(EffectiveCommandId(submission.CommandId));
    }

    /// <inheritdoc />
    public Task<ExerciseOutcome<ContractStreamEvent<T>>> TrySubmitAndWaitForReassignmentAsync<T>(
        ReassignmentSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType
    {
        ArgumentNullException.ThrowIfNull(submission);
        return Task.FromResult(
            AdvancingLedgerEndOnCommit(
                Staged<ExerciseOutcome<ContractStreamEvent<T>>>(
                    _canton.ReassignmentResults, typeof(T), "reassignment outcome", $"WithReassignmentResult<{typeof(T).Name}>")));
    }

    /// <inheritdoc />
    public Task<ExerciseOutcome<TransactionTree>> TrySubmitAndWaitForTransactionTreeAsync(
        CommandsSubmission submission,
        SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        return Task.FromResult(
            AdvancingLedgerEndOnCommit(
                _canton.TransactionTreeOutcome ?? throw StagingMissing(
                    "transaction tree outcome",
                    nameof(TrySubmitAndWaitForTransactionTreeAsync),
                    "WithTransactionTree")));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<CompletionStreamEvent> CompletionStreamAsync(
        SubmitterInfo submitter,
        long beginExclusiveOffset = 0L,
        CancellationToken cancellationToken = default) =>
        Replay(StagedCompletions(), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ConnectedSynchronizer>> GetConnectedSynchronizersAsync(
        Party? party = null,
        string? participantId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_canton.ConnectedSynchronizers ?? throw StagingMissing(
            "connected synchronizers", nameof(GetConnectedSynchronizersAsync), "WithConnectedSynchronizers"));

    /// <inheritdoc />
    public Task<string> GetLedgerApiVersionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_canton.LedgerApiVersion ?? throw StagingMissing(
            "Ledger API version", nameof(GetLedgerApiVersionAsync), "WithLedgerApiVersion"));

    /// <inheritdoc />
    public Task<TransactionResult> GetUpdateByOffsetAsync(
        long offset,
        SubmitterInfo submitter,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(offset);
        return Task.FromResult(_canton.UpdatesByOffset.TryGetValue(offset, out var result)
            ? result
            : throw StagingMissing($"update at offset {offset}", nameof(GetUpdateByOffsetAsync), "WithUpdateByOffset"));
    }

    /// <inheritdoc />
    public Task<TransactionResult> GetUpdateByIdAsync(
        string updateId,
        SubmitterInfo submitter,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(updateId);
        return Task.FromResult(_canton.UpdatesById.TryGetValue(updateId, out var result)
            ? result
            : throw StagingMissing($"update with id '{updateId}'", nameof(GetUpdateByIdAsync), "WithUpdateById"));
    }

    /// <inheritdoc />
    public Task<TrafficCostEstimate?> EstimateTrafficCostAsync(
        CommandsSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        return Task.FromResult(_canton.TrafficCostEstimate is { } staged
            ? staged.Estimate
            : throw StagingMissing(
                "traffic-cost estimate", nameof(EstimateTrafficCostAsync), "WithTrafficCostEstimate"));
    }

    private static CommandId EffectiveCommandId(CommandId? submitted) =>
        submitted ?? new CommandId(Guid.NewGuid().ToString());

    private IReadOnlyList<CompletionStreamEvent> StagedCompletions() =>
        _canton.CompletionEvents ?? throw StagingMissing(
            "completion events", nameof(CompletionStreamAsync), "WithCompletionEvents");

    private static NotSupportedException StagingMissing(string what, string member, string builderCall) =>
        new($"FakeLedgerClient has no {what} staged for '{member}'. Stage it with " +
            $"FakeLedgerClient.Create().{builderCall}(...).Build() before exercising this path.");
}

/// <summary>
/// The Canton-specific behaviour a <see cref="FakeLedgerClient"/> replays beyond the neutral
/// <see cref="Daml.Ledger.Abstractions.ILedgerClient"/> surface: staged reassignment and
/// transaction-tree outcomes, completion-stream events, connected synchronizers, the Ledger API
/// version, the traffic-cost estimate, and point-read transactions keyed by offset and id.
/// </summary>
internal sealed record FakeCantonSurface(
    IReadOnlyDictionary<Type, object> ReassignmentResults,
    ExerciseOutcome<TransactionTree>? TransactionTreeOutcome,
    IReadOnlyList<CompletionStreamEvent>? CompletionEvents,
    IReadOnlyList<ConnectedSynchronizer>? ConnectedSynchronizers,
    string? LedgerApiVersion,
    StagedTrafficCostEstimate? TrafficCostEstimate,
    IReadOnlyDictionary<long, TransactionResult> UpdatesByOffset,
    IReadOnlyDictionary<string, TransactionResult> UpdatesById);

/// <summary>
/// A staged traffic-cost answer, boxed so that staging "the participant served no estimation" is
/// distinguishable from staging nothing at all.
/// </summary>
/// <param name="Estimate">The estimate to reply with, or <see langword="null"/> for a participant
/// that served no estimation.</param>
internal sealed record StagedTrafficCostEstimate(TrafficCostEstimate? Estimate);
