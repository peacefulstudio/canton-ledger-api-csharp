// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Data;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// The transport-neutral command-completion payload the participant records for a
/// submitted command, carrying the fields that are present whatever the verdict.
/// The verdict itself is modelled by the enclosing
/// <see cref="CompletionStreamEvent"/> variant — <see cref="CompletionStreamEvent.CommandAccepted"/>
/// (which adds the update id) or <see cref="CompletionStreamEvent.CommandRejected"/>
/// (which adds the rejection <see cref="CompletionStatus"/>) — so a caller reads the
/// verdict off the event type rather than off this payload.
/// </summary>
/// <param name="CommandId">The effective command id the participant recorded, for
/// correlating the completion with a prior submission. The Ledger API marks the field
/// required on a completion, so a transport rejects a completion whose wire command id
/// is absent as a malformed response rather than handing back a placeholder — this value
/// is always readable.</param>
/// <param name="Offset">The participant's ledger offset for this completion — persist
/// it as the resume offset (exclusive) for a subsequent completion stream.</param>
/// <param name="ActAs">The submitter parties whose command produced this completion.</param>
/// <param name="SynchronizerTime">The synchronizer and record time the completion was
/// sequenced at.</param>
/// <param name="SubmissionId">The submission id the caller supplied, or null when none
/// was set.</param>
/// <param name="UserId">The participant user id the command was submitted as, or null
/// when none was set.</param>
/// <param name="DeduplicationOffset">The deduplication-period start offset, when the
/// participant reported the period as an offset; null when it reported a duration or
/// nothing.</param>
/// <param name="DeduplicationDuration">The deduplication-period duration, when the
/// participant reported the period as a duration; null when it reported an offset or
/// nothing.</param>
public sealed record Completion(
    CommandId CommandId,
    long Offset,
    IReadOnlyList<Party> ActAs,
    SynchronizerTime SynchronizerTime,
    string? SubmissionId,
    string? UserId,
    long? DeduplicationOffset,
    TimeSpan? DeduplicationDuration);

/// <summary>
/// The verdict a rejected command completed with, as a <c>google.rpc.Code</c> value —
/// the same code space on both the gRPC and HTTP transports.
/// </summary>
/// <param name="Code">The <c>google.rpc.Code</c> status code; non-zero for a rejection.</param>
/// <param name="Message">The human-readable rejection detail from the participant.</param>
public sealed record CompletionStatus(int Code, string Message);

/// <summary>
/// The synchronizer a completion was sequenced on, together with the record time it was
/// sequenced at.
/// </summary>
/// <param name="SynchronizerId">The id of the synchronizer that sequenced the completion.</param>
/// <param name="RecordTime">The record time the completion was sequenced at.</param>
public sealed record SynchronizerTime(string SynchronizerId, DateTimeOffset RecordTime);
