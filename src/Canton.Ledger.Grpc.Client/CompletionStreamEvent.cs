// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.Daml.Ledger.Api.V2;

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// An event observed on the command completion stream
/// (<see cref="LedgerClient.CompletionStreamAsync"/>). Discriminated union:
/// callers <c>switch</c> on the concrete subtype, mirroring
/// <c>ContractStreamEvent&lt;T&gt;</c> on the update stream.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><see cref="CommandCompleted"/> — the participant recorded a
///   verdict for a submitted command; carries the raw
///   <see cref="Com.Daml.Ledger.Api.V2.Completion"/> primitive (ADR 0007).</item>
///   <item><see cref="Checkpoint"/> — a participant-emitted offset checkpoint
///   carrying no completion payload. Consumers persist
///   <see cref="Checkpoint.Offset"/> to advance their resume offset during
///   quiet periods (no completions arriving), avoiding the
///   resume-from-stale-offset failure mode (re-processing, or
///   <c>PARTICIPANT_PRUNED_DATA_ACCESSED</c> once the participant prunes),
///   and to detect command timeout: the ledger has progressed past the
///   checkpoint offset without completing the command.</item>
///   <item><see cref="StreamError"/> — a mid-stream transport fault, surfaced
///   in-band as a terminal event rather than thrown, mirroring
///   <c>ContractStreamEvent&lt;T&gt;.StreamError</c> on the update stream and
///   <c>AcsSnapshotEntry&lt;T&gt;.StreamError</c> on the ACS snapshot (ADR 0015).</item>
/// </list>
/// </remarks>
public abstract record CompletionStreamEvent
{
    /// <summary>Sealed; new variants live alongside the existing ones.</summary>
    private protected CompletionStreamEvent() { }

    /// <summary>
    /// The participant recorded a verdict for a submitted command.
    /// </summary>
    /// <param name="Completion">The raw completion primitive; correlate by
    /// <c>command_id</c>/<c>submission_id</c> and persist
    /// <c>Completion.Offset</c> as the resume offset.</param>
    public sealed record CommandCompleted(Completion Completion) : CompletionStreamEvent;

    /// <summary>
    /// A participant-emitted offset checkpoint with no completion payload,
    /// emitted on a participant-configured cadence
    /// (<c>max_offset_checkpoint_emission_delay</c>) regardless of command
    /// activity.
    /// </summary>
    /// <param name="Offset">The participant's current ledger offset — persist
    /// it as the resume offset (exclusive) for a subsequent
    /// <see cref="LedgerClient.CompletionStreamAsync"/> call.</param>
    public sealed record Checkpoint(long Offset) : CompletionStreamEvent;

    /// <summary>
    /// The completion stream failed mid-flight. Surfaced in-band as a terminal
    /// event rather than thrown, so a caller draining the stream with
    /// <c>await foreach</c> decides policy — reopen from the last persisted
    /// offset, log, or stop — with the same value-not-exception handling the
    /// update stream uses for <c>ContractStreamEvent&lt;T&gt;.StreamError</c>
    /// (ADR 0015). Terminal: no further events follow.
    /// </summary>
    /// <param name="StatusCode">Transport status code from the failed call. For
    /// gRPC this is <c>(int)Grpc.Core.StatusCode</c>; consumers that want the
    /// typed enum cast back.</param>
    /// <param name="Message">Status detail / message from the participant or transport.</param>
    public sealed record StreamError(int StatusCode, string Message) : CompletionStreamEvent;
}
