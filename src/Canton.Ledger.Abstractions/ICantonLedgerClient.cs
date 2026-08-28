// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// The Canton participant client surface: everything on <see cref="ILedgerClient"/>
/// plus the operations that are specific to a Canton node and absent from the upstream
/// abstraction — fire-and-forget submission, the command completion stream,
/// connected-synchronizer and Ledger API version discovery, offset/id point reads,
/// tree-shaped submission, and traffic-cost estimation.
/// This is the type registered in dependency injection (alongside <see cref="ILedgerClient"/>,
/// which resolves to the same instance), so consumers of the flagship fire path
/// reach these operations through the injected abstraction without downcasting to the
/// concrete <c>LedgerClient</c> — keeping the client mockable and decoratable.
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
    /// A true fire path with no client-side pending-set: the consumer
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
    /// <c>SubscribeAsync</c> or <see cref="CompletionStreamAsync"/>, not awaited here.
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
    /// <see cref="ExerciseOutcome{T}"/> — one typed representation whether a reassignment
    /// is observed or caused. Source and target synchronizer ids are required on the command.
    /// </summary>
    /// <typeparam name="T">The template or interface marker the resulting contract is projected as.</typeparam>
    /// <param name="submission">The reassignment submission to submit and await.</param>
    /// <param name="timeout">
    /// Per-call deadline for the submit-and-wait RPC. Takes precedence over
    /// <c>LedgerClientOptions.Timeout</c>; null uses the configured default. Overrunning it
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
    /// Submits commands, waits for the resulting transaction, and returns it with its parent/child
    /// hierarchy intact — which exercise caused which sub-creates and sub-exercises.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tree-shaped counterpart to
    /// <see cref="ILedgerWriter.TrySubmitAndWaitForTransactionAsync(RuntimeCommands.CommandsSubmission, RuntimeCommands.SubmitterInfo, TimeSpan?, CancellationToken)"/>,
    /// which returns the same transaction flattened into separate created/archived/exercised lists.
    /// Neither shape is a superset of the other on the wire: the flat overload takes the participant's
    /// ACS-delta view (creates and archives), while hierarchy is only meaningful over the ledger-effects
    /// view (creates and exercises), which this method always requests. Callers that want the flattened
    /// shape as well project the tree with <see cref="TransactionTreeExtensions.ToTransactionResult"/>
    /// rather than submitting twice.
    /// </para>
    /// <para>
    /// Only events the submitting parties are entitled to see are returned. An event whose parent
    /// exercise the participant filtered out attaches to the nearest enclosing exercise those parties
    /// can still see, or surfaces as a root when none remains.
    /// </para>
    /// <para>
    /// A transaction whose events cannot describe a tree yields
    /// <see cref="ExerciseOutcome{T}.InfraError"/> carrying the reason, never a silently wrong tree.
    /// </para>
    /// </remarks>
    /// <param name="submission">The commands to submit.</param>
    /// <param name="submitter">The parties to submit as, and to read the resulting events as.</param>
    /// <param name="timeout">Overrides the client's configured request timeout when supplied.</param>
    /// <param name="cancellationToken">Cancels the submission.</param>
    /// <returns>
    /// <see cref="ExerciseOutcome{T}.One"/> carrying the committed transaction as a tree,
    /// <see cref="ExerciseOutcome{T}.DamlError"/> when the participant rejected the commands, or
    /// <see cref="ExerciseOutcome{T}.InfraError"/> on a transport failure, a per-call timeout, or a
    /// response that cannot describe a tree.
    /// </returns>
    Task<ExerciseOutcome<TransactionTree>> TrySubmitAndWaitForTransactionTreeAsync(
        RuntimeCommands.CommandsSubmission submission,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Materializes the active-contract-set snapshot for a Daml interface, decoding each row's
    /// participant-computed interface view into <typeparamref name="TView"/>. The gRPC counterpart
    /// of <see cref="IPqsClient.QueryAsync{TInterface, TView}(CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Drains <see cref="ILedgerStreamer.SubscribeActiveAsync{T}"/> over
    /// <typeparamref name="TInterface"/> — which already asks the participant for an
    /// <c>InterfaceFilter</c> with the view included, and already projects the view record
    /// (not the implementing template's create-arguments) onto each snapshot row — and decodes
    /// that record through the generated view type's <c>FromRecord</c> factory. Both type
    /// arguments are explicit (<c>QueryActiveAsync&lt;IHolding, HoldingView&gt;(party)</c>)
    /// because the queried interface and its view are distinct types; the
    /// <see cref="IHasView{TView}"/> constraint ties them.
    /// </para>
    /// <para>
    /// This is a materializing convenience, not a streaming read: it cannot hand a fault back
    /// in-band, so a snapshot that faults, carries a row the projector could not classify, ends
    /// without its terminal checkpoint, or carries a view that does not decode into
    /// <typeparamref name="TView"/> throws <see cref="LedgerOperationException"/> rather than
    /// returning a short list that looks complete. The in-band terminal-<c>StreamError</c>
    /// contract binds the <c>await foreach</c> streaming surfaces; stay on
    /// <see cref="ILedgerStreamer.SubscribeActiveAsync{T}"/> for value-shaped fault handling or
    /// when the snapshot's resume ticket matters, since the terminal checkpoint is consumed and
    /// discarded here.
    /// </para>
    /// <para>
    /// Both shipped transports project the participant-computed view, so both serve this method.
    /// A snapshot row whose view the participant did not compute — an absent view, or one carrying
    /// a <c>viewStatus</c> other than <c>OK</c> — reaches the drain as an unclassified row and
    /// throws <see cref="LedgerOperationException"/> rather than yielding an empty view record.
    /// </para>
    /// </remarks>
    /// <typeparam name="TInterface">The generated Daml interface marker (e.g. <c>IHolding</c>).</typeparam>
    /// <typeparam name="TView">The interface's view record (e.g. <c>HoldingView</c>).</typeparam>
    /// <param name="submitter">The submitter authorization whose combined parties scope visibility.</param>
    /// <param name="activeAtOffset">Snapshot offset; <see langword="null"/> means the current ledger end.</param>
    /// <param name="cancellationToken">Cancels the underlying snapshot stream cleanly.</param>
    /// <exception cref="LedgerOperationException">
    /// The snapshot faulted, carried an unclassified row, carried a view that did not decode into
    /// <typeparamref name="TView"/>, or ended without its terminal checkpoint.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<IReadOnlyList<InterfaceContract<TInterface, TView>>> QueryActiveAsync<TInterface, TView>(
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord =>
        InterfaceViewSnapshot.DrainAsync<TInterface, TView>(
            SubscribeActiveAsync<TInterface>(submitter, activeAtOffset, cancellationToken),
            cancellationToken);

    /// <summary>
    /// Streams command completions for the submitter's parties as they arrive,
    /// surfacing each response as a <see cref="CompletionStreamEvent"/>:
    /// <see cref="CompletionStreamEvent.CommandAccepted"/> carries the neutral
    /// <see cref="Completion"/> payload and the resulting update id for an accepted
    /// command, <see cref="CompletionStreamEvent.CommandRejected"/> carries the neutral
    /// <see cref="Completion"/> and the rejection <see cref="CompletionStatus"/>,
    /// <see cref="CompletionStreamEvent.Checkpoint"/> carries the participant's offset
    /// checkpoints so the resume offset keeps advancing during quiet periods, and a
    /// mid-stream transport fault or a completion whose payload cannot be decoded is
    /// surfaced in-band as a terminal
    /// <see cref="CompletionStreamEvent.StreamError"/> rather than thrown,
    /// at parity with the update stream. The consumer correlates each completion by
    /// <see cref="Completion.CommandId"/> and persists its own offset; the client holds
    /// no correlation or recovery state. To catch the completion of a command you are
    /// about to <see cref="SubmitAsync"/>, capture the offset before submitting and pass
    /// it as <paramref name="beginExclusiveOffset"/> — a completion can be emitted before
    /// the stream is opened. A caller cancelling via <paramref name="cancellationToken"/>
    /// gets an <see cref="OperationCanceledException"/>, not a
    /// <see cref="CompletionStreamEvent.StreamError"/>.
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

    /// <summary>
    /// Asks the participant what <paramref name="submission"/> would cost in synchronizer traffic,
    /// without submitting it and without changing ledger state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The estimate comes from the participant's interactive-submission prepare step, so the commands
    /// are fully interpreted to produce it: invalid commands fail here exactly as they would on
    /// submission, and the call costs about what a submission costs. Authorization is looser than
    /// submitting, though — the caller's token needs only <em>read</em> rights for the parties in
    /// <see cref="RuntimeCommands.CommandsSubmission.ActAs"/>, not act rights, because nothing is
    /// executed. The prepared transaction is discarded; this call prices a submission rather than
    /// beginning an external-signing flow.
    /// </para>
    /// <para>
    /// The workflow id on <paramref name="submission"/> is not carried — the prepare step has no field
    /// for it.
    /// </para>
    /// </remarks>
    /// <param name="submission">The commands to price, exactly as they would be submitted.</param>
    /// <param name="timeout">
    /// Per-call deadline overriding the client's configured request timeout.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The participant's estimate, or <see langword="null"/> when it returned none — a participant may
    /// omit the estimation, and one with traffic control disabled does. An estimation that is present
    /// but reports zero is a zero-cost estimate, not an absent one.
    /// </returns>
    Task<TrafficCostEstimate?> EstimateTrafficCostAsync(
        RuntimeCommands.CommandsSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
