// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Abstractions;

/// <summary>
/// An event observed on the command completion stream
/// (<see cref="ICantonLedgerClient.CompletionStreamAsync"/>). Discriminated union:
/// callers <c>switch</c> on the concrete subtype, mirroring
/// <c>ContractStreamEvent&lt;T&gt;</c> on the update stream. The verdict is modelled as
/// the event type — <see cref="CommandAccepted"/> versus <see cref="CommandRejected"/> —
/// so illegal states are unrepresentable: an update id is present only on an accepted
/// command, a rejection status only on a rejected one.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><see cref="CommandAccepted"/> — the participant accepted a submitted command;
///   carries the neutral <see cref="Completion"/> payload and the resulting update id.</item>
///   <item><see cref="CommandRejected"/> — the participant rejected a submitted command;
///   carries the neutral <see cref="Completion"/> payload and the rejection
///   <see cref="CompletionStatus"/>.</item>
///   <item><see cref="Checkpoint"/> — a participant-emitted offset checkpoint
///   carrying no completion payload. Consumers persist
///   <see cref="Checkpoint.Offset"/> to advance their resume offset during
///   quiet periods (no completions arriving), avoiding the
///   resume-from-stale-offset failure mode (re-processing, or
///   <c>PARTICIPANT_PRUNED_DATA_ACCESSED</c> once the participant prunes),
///   and to detect command timeout: the ledger has progressed past the
///   checkpoint offset without completing the command.</item>
///   <item><see cref="StreamError"/> — a condition that terminated the stream
///   abnormally: a mid-stream transport fault, or a payload the client could
///   not decode. Surfaced in-band as a terminal event rather than thrown,
///   mirroring <c>ContractStreamEvent&lt;T&gt;.StreamError</c> on the update
///   stream and <c>AcsSnapshotEntry&lt;T&gt;.StreamError</c> on the ACS
///   snapshot.</item>
/// </list>
/// </remarks>
public abstract record CompletionStreamEvent
{
    /// <summary>Sealed; new variants live alongside the existing ones.</summary>
    private protected CompletionStreamEvent() { }

    /// <summary>
    /// The participant accepted a submitted command.
    /// </summary>
    /// <param name="Completion">The neutral completion payload; correlate by
    /// <see cref="Completion.CommandId"/> and persist <see cref="Completion.Offset"/> as
    /// the resume offset.</param>
    /// <param name="UpdateId">The id of the update the accepted command produced.</param>
    public sealed record CommandAccepted(Completion Completion, string UpdateId) : CompletionStreamEvent;

    /// <summary>
    /// The participant rejected a submitted command.
    /// </summary>
    /// <param name="Completion">The neutral completion payload; correlate by
    /// <see cref="Completion.CommandId"/> and persist <see cref="Completion.Offset"/> as
    /// the resume offset.</param>
    /// <param name="Status">The rejection verdict — a non-zero <c>google.rpc.Code</c> and
    /// its detail.</param>
    public sealed record CommandRejected(Completion Completion, CompletionStatus Status) : CompletionStreamEvent;

    /// <summary>
    /// A participant-emitted offset checkpoint with no completion payload,
    /// emitted on a participant-configured cadence
    /// (<c>max_offset_checkpoint_emission_delay</c>) regardless of command
    /// activity.
    /// </summary>
    /// <param name="Offset">The participant's current ledger offset — persist
    /// it as the resume offset (exclusive) for a subsequent
    /// <see cref="ICantonLedgerClient.CompletionStreamAsync"/> call.</param>
    public sealed record Checkpoint(long Offset) : CompletionStreamEvent;

    /// <summary>
    /// The completion stream failed mid-flight. Surfaced in-band as a terminal
    /// event rather than thrown, so a caller draining the stream with
    /// <c>await foreach</c> decides policy — reopen from the last persisted
    /// offset, log, or stop — with the same value-not-exception handling the
    /// update stream uses for <c>ContractStreamEvent&lt;T&gt;.StreamError</c>.
    /// Terminal: no further events follow.
    /// </summary>
    /// <param name="StatusCode">Transport-native status code — what the transport
    /// actually said, never a translation: <c>(int)Grpc.Core.StatusCode</c> over
    /// gRPC (consumers that want the typed enum cast back), the HTTP status over
    /// REST. <c>0</c> means the call itself succeeded but a payload could not be
    /// decoded — neither a valid gRPC error nor a valid HTTP status, so it
    /// identifies a decode failure unambiguously on either transport.</param>
    /// <param name="Message">Status detail / message from the participant or transport.</param>
    public sealed record StreamError(int StatusCode, string Message) : CompletionStreamEvent;
}
