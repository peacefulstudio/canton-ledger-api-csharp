// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// The Canton participant client surface: everything on <see cref="ILedgerClient"/>
/// plus the operations that are specific to a Canton node and absent from the upstream
/// abstraction — fire-and-forget submission, the command completion stream,
/// connected-synchronizer and Ledger API version discovery, and offset/id point reads.
/// This is the type registered in dependency injection (alongside <see cref="ILedgerClient"/>,
/// which resolves to the same instance), so consumers of the flagship fire path (ADR 0007)
/// reach these operations through the injected abstraction without downcasting to the
/// concrete <see cref="LedgerClient"/> — keeping the client mockable and decoratable.
/// </summary>
public interface ICantonLedgerClient : ILedgerClient
{
    /// <summary>
    /// Fire-and-forget submission: hands the commands to the participant and
    /// returns once they are accepted for processing, yielding the
    /// <c>command_id</c> for correlating the eventual completion. The verdict
    /// on the transaction itself is not awaited here — observe it on
    /// <see cref="CompletionStreamAsync"/>.
    /// </summary>
    /// <remarks>
    /// A true fire path with no client-side pending-set (ADR 0007): the consumer
    /// correlates completions by <c>command_id</c>/<c>submission_id</c> and owns
    /// its own offset. The returned <see cref="RuntimeCommands.CommandId"/> is the
    /// effective id the participant recorded — minted here when the submission
    /// omits one — so a caller retrying after a transport failure (by hand or via
    /// a resilience policy such as Polly) must resubmit with this same id for
    /// ledger-side deduplication. Re-invoking with a fresh, command_id-less
    /// submission instead mints a new id and double-submits, because the
    /// participant may have accepted the first attempt before the failure
    /// surfaced.
    /// </remarks>
    Task<RuntimeCommands.CommandId> SubmitAsync(
        RuntimeCommands.CommandsSubmission submission,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fire-and-forget reassignment submission: submits an <see cref="UnassignCommand"/> or
    /// <see cref="AssignCommand"/> through <c>CommandSubmissionService.SubmitReassignment</c> and
    /// returns once the participant accepts it, yielding the <c>command_id</c> for correlating the
    /// eventual completion. The resulting <c>Unassigned</c>/<c>Assigned</c> event is observed on
    /// <c>SubscribeAsync</c> or <see cref="CompletionStreamAsync"/>, not awaited here (ADR 0007).
    /// Source and target synchronizer ids are required on the command.
    /// </summary>
    /// <remarks>
    /// The two-step unassign→assign dance is the consumer's: capture the <c>reassignment_id</c> from
    /// the resulting <c>UnassignedEvent</c> and pass it to a follow-up <see cref="AssignCommand"/>.
    /// The returned <see cref="RuntimeCommands.CommandId"/> is the effective id the participant
    /// recorded — minted here when omitted — so a retry after a transport failure must resubmit with
    /// this same id for ledger-side deduplication.
    /// </remarks>
    Task<RuntimeCommands.CommandId> SubmitReassignmentAsync(
        ReassignmentSubmission submission,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits an <see cref="UnassignCommand"/> or <see cref="AssignCommand"/> through
    /// <c>CommandService.SubmitAndWaitForReassignment</c> and awaits the resulting reassignment,
    /// projecting it into the typed read-side <see cref="ContractStreamEvent{T}.Unassigned"/> /
    /// <see cref="ContractStreamEvent{T}.Assigned"/> variant wrapped in the shared
    /// <see cref="ExerciseOutcome{T}"/> (ADR 0007) — one typed representation whether a reassignment
    /// is observed or caused. Source and target synchronizer ids are required on the command.
    /// </summary>
    /// <typeparam name="T">The template or interface marker the resulting contract is projected as.</typeparam>
    /// <param name="submission">The reassignment submission to submit and await.</param>
    /// <param name="timeout">
    /// Per-call deadline for the submit-and-wait RPC (ADR 0016). Takes precedence over
    /// <see cref="LedgerClientOptions.Timeout"/>; null uses the configured default. Overrunning it
    /// surfaces as an <see cref="ExerciseOutcome{T}.InfraError"/>, never as caller cancellation.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The projected <c>Unassigned</c>/<c>Assigned</c> variants now carry the <c>reassignment_id</c>
    /// and <c>reassignment_counter</c> the participant reported, so a consumer driving the
    /// unassign→assign dance reads the id straight off the returned event.
    /// </remarks>
    Task<ExerciseOutcome<ContractStreamEvent<T>>> TrySubmitAndWaitForReassignmentAsync<T>(
        ReassignmentSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType;

    /// <summary>
    /// Streams command completions for the submitter's parties as they arrive,
    /// surfacing each response as a <see cref="CompletionStreamEvent"/>:
    /// <see cref="CompletionStreamEvent.CommandCompleted"/> wraps the raw
    /// <see cref="Com.Daml.Ledger.Api.V2.Completion"/> primitive (ADR 0007),
    /// <see cref="CompletionStreamEvent.Checkpoint"/> carries the
    /// participant's offset checkpoints so the resume offset keeps advancing
    /// during quiet periods, and a mid-stream transport fault is surfaced in-band
    /// as a terminal <see cref="CompletionStreamEvent.StreamError"/> rather than
    /// thrown (ADR 0015), at parity with the update stream. The consumer
    /// correlates each completion by <c>command_id</c>/<c>submission_id</c> and
    /// persists its own offset; the client holds no correlation or recovery
    /// state. To catch the completion of a command you are about to
    /// <see cref="SubmitAsync"/>, capture the offset before submitting and pass
    /// it as <paramref name="beginExclusiveOffset"/> — a completion can be
    /// emitted before the stream is opened. A caller cancelling via
    /// <paramref name="cancellationToken"/> gets an
    /// <see cref="OperationCanceledException"/>, not a <c>StreamError</c>.
    /// </summary>
    IAsyncEnumerable<CompletionStreamEvent> CompletionStreamAsync(
        RuntimeCommands.SubmitterInfo submitter,
        long beginExclusiveOffset = 0L,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the synchronizers the participant is currently connected to.
    /// </summary>
    /// <param name="party">
    /// Optional party whose connection permissions scope the result. Null returns
    /// every synchronizer the participant is connected to, with an unspecified
    /// permission on each entry.
    /// </param>
    /// <param name="participantId">
    /// Optional participant id, for a participant querying another participant's
    /// mapping. Null defaults to the host participant.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<ConnectedSynchronizer>> GetConnectedSynchronizersAsync(
        Party? party = null,
        string? participantId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the Ledger API version reported by the participant.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string> GetLedgerApiVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a single update by its absolute offset, projected the same way as
    /// <see cref="ILedgerWriter.TrySubmitAndWaitForTransactionAsync"/>'s success case. The
    /// <paramref name="submitter"/>'s combined <c>ActAs ∪ ReadAs</c> parties scope
    /// visibility, with no template/interface restriction — every event those
    /// parties witness on the update is returned.
    /// </summary>
    /// <param name="offset">The absolute offset of the update to look up. Must be positive.</param>
    /// <param name="submitter">The parties whose visibility scopes the lookup.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="offset"/> is zero or negative.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The update at <paramref name="offset"/> is a reassignment or topology
    /// transaction rather than a ledger transaction, or the transaction payload
    /// is malformed (a required field is unset, or a value cannot be decoded) and
    /// cannot be projected.
    /// </exception>
    Task<TransactionResult> GetUpdateByOffsetAsync(
        long offset,
        RuntimeCommands.SubmitterInfo submitter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a single update by its update id, projected the same way as
    /// <see cref="ILedgerWriter.TrySubmitAndWaitForTransactionAsync"/>'s success case. The
    /// <paramref name="submitter"/>'s combined <c>ActAs ∪ ReadAs</c> parties scope
    /// visibility, with no template/interface restriction — every event those
    /// parties witness on the update is returned.
    /// </summary>
    /// <param name="updateId">The id of the update to look up.</param>
    /// <param name="submitter">The parties whose visibility scopes the lookup.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// The update with <paramref name="updateId"/> is a reassignment or topology
    /// transaction rather than a ledger transaction, or the transaction payload
    /// is malformed (a required field is unset, or a value cannot be decoded) and
    /// cannot be projected.
    /// </exception>
    Task<TransactionResult> GetUpdateByIdAsync(
        string updateId,
        RuntimeCommands.SubmitterInfo submitter,
        CancellationToken cancellationToken = default);
}
