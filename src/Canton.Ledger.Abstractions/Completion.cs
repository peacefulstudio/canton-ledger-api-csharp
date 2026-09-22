// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
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
/// <param name="PaidTrafficCost">The traffic the submission actually paid for, in bytes.
/// It is the <em>confirmation-request cost only</em>, not a total, so the estimate it is
/// comparable to is <see cref="TrafficCostEstimate.ConfirmationRequestCost"/> — never
/// <see cref="TrafficCostEstimate.TotalCost"/>, which also counts the confirmation
/// responses and will always look larger.
/// <para>
/// Not nullable, because the wire has no presence to carry: <c>completion.proto</c>
/// declares <c>paid_traffic_cost</c> as a bare <c>int64</c>, so a participant that reports
/// nothing and a participant that reports a genuine zero are indistinguishable over gRPC.
/// Modelling the field as nullable would invent a distinction one transport cannot make
/// and would put the two transports' projections of the same completion at odds. A zero
/// therefore means "nothing was charged, or nothing was said"; read it as a floor, not as
/// a confirmed charge. The sibling fields on the update carriers do declare presence
/// (<c>optional int64</c> on <c>transaction.proto</c> and <c>reassignment.proto</c>), so a
/// neutral shape for those may model absence when it gains one.
/// </para></param>
/// <param name="TraceContext">The W3C trace context the participant propagated with this
/// completion, for joining the submission to a distributed trace; null when the completion
/// carried none, and also when it carried a trace context that names no
/// <see cref="Abstractions.TraceContext.Traceparent"/> — a tracestate without a traceparent
/// identifies no span and is not a context a caller can continue.</param>
public sealed record Completion(
    CommandId CommandId,
    long Offset,
    EquatableArray<Party> ActAs,
    SynchronizerTime SynchronizerTime,
    string? SubmissionId,
    string? UserId,
    long? DeduplicationOffset,
    TimeSpan? DeduplicationDuration,
    long PaidTrafficCost,
    TraceContext? TraceContext);

/// <summary>
/// The W3C trace context a participant propagated alongside a completion, in the neutral
/// shape both transports project into — the protobuf <c>com.daml.ledger.api.v2.TraceContext</c>
/// over gRPC and the <c>traceContext</c> object over HTTP.
/// </summary>
/// <remarks>
/// A trace context only ever reaches a caller with a <see cref="Traceparent"/>: a wire
/// trace context that names none is projected as a null
/// <see cref="Completion.TraceContext"/> rather than as an instance whose traceparent is
/// empty, so holding one of these means holding a span to continue. The invariant is
/// enforced on construction and on a <c>with</c> expression alike.
/// </remarks>
/// <param name="Traceparent">The W3C <c>traceparent</c> header value — the version, trace
/// id, parent span id and trace flags of the span the submission was traced under. Must be
/// neither null, empty nor whitespace.</param>
/// <param name="Tracestate">The W3C <c>tracestate</c> header value carrying vendor-specific
/// trace data, or null when the participant propagated none.</param>
/// <exception cref="ArgumentNullException">Thrown when <paramref name="Traceparent"/> is
/// null.</exception>
/// <exception cref="ArgumentException">Thrown when <paramref name="Traceparent"/> is empty
/// or consists only of whitespace.</exception>
public sealed record TraceContext(string Traceparent, string? Tracestate)
{
    /// <summary>The W3C <c>traceparent</c> header value; never null, empty or whitespace.</summary>
    public string Traceparent
    {
        get;
        init => field = RequireTraceparent(value);
    } = RequireTraceparent(Traceparent);

    private static string RequireTraceparent(string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(Traceparent));
        return value;
    }
}

/// <summary>
/// The verdict a rejected command completed with, as a <c>google.rpc.Code</c> value —
/// the same code space on both the gRPC and HTTP transports — together with the structured
/// rejection detail the participant packed into <c>google.rpc.Status.details</c>.
/// </summary>
/// <remarks>
/// <para>
/// Only the <c>google.rpc.ErrorInfo</c> detail is projected, in the same shape a parsed
/// Canton error already reaches a caller in: <see cref="ErrorId"/> carries its
/// <c>reason</c> — the Canton error id, the value
/// <see cref="ParsedLedgerError.ReportedErrorId"/> reports — and <see cref="Metadata"/>
/// carries its metadata map verbatim, the same map
/// <see cref="ParsedLedgerError.Structured.Metadata"/> carries. Every other detail type
/// (<c>RetryInfo</c>, <c>RequestInfo</c>, <c>ResourceInfo</c> and any type a future
/// participant adds) is skipped: nothing on this record reports whether one was present.
/// The first <c>ErrorInfo</c> among the details is the one projected, should a participant
/// ever pack several.
/// </para>
/// <para>
/// A rejection detail is best-effort, never a failure: a status carrying no details, no
/// <c>ErrorInfo</c> among them, or an <c>ErrorInfo</c> whose bytes do not decode reaches a
/// caller as a null <see cref="ErrorId"/> and an empty <see cref="Metadata"/> rather than
/// as an exception. The <see cref="Code"/> and <see cref="Message"/> of such a rejection are
/// still projected, so a caller always has the verdict even when the detail is unreadable.
/// </para>
/// </remarks>
/// <param name="Code">The <c>google.rpc.Code</c> status code; non-zero for a rejection.</param>
/// <param name="Message">The human-readable rejection detail from the participant.</param>
/// <param name="ErrorId">The Canton error id the rejection's <c>ErrorInfo</c> named, such as
/// <c>CONTRACT_NOT_FOUND</c>; null when the rejection carried no readable <c>ErrorInfo</c>,
/// and also when it carried one naming an empty reason, which identifies no error.</param>
/// <param name="Metadata">The metadata map of the rejection's <c>ErrorInfo</c> — the
/// <c>category</c>, <c>definite_answer</c> and error-specific entries the participant
/// attached. Empty, never null, when the rejection carried no readable <c>ErrorInfo</c>; a
/// null argument is read as an empty map, on construction and on a <c>with</c> expression
/// alike, so that two statuses are always comparable.</param>
public sealed record CompletionStatus(
    int Code,
    string Message,
    string? ErrorId,
    IReadOnlyDictionary<string, string> Metadata)
{
    private static readonly IReadOnlyDictionary<string, string> NoMetadata =
        FrozenDictionary<string, string>.Empty;

    /// <summary>The <c>ErrorInfo</c> metadata map; empty, never null, when the rejection
    /// carried no readable <c>ErrorInfo</c>.</summary>
    public IReadOnlyDictionary<string, string> Metadata
    {
        get;
        init => field = value ?? NoMetadata;
    } = Metadata ?? NoMetadata;

    /// <summary>
    /// Compares two statuses by value, reading <see cref="Metadata"/> as its entries rather
    /// than as a map reference, so that the same rejection projected off either transport
    /// compares equal however each wire format happened to order the keys.
    /// </summary>
    /// <param name="other">The status to compare with, or null.</param>
    /// <returns>True when both statuses carry the same code, message, error id and metadata
    /// entries.</returns>
    public bool Equals(CompletionStatus? other) =>
        other is not null
        && Code == other.Code
        && string.Equals(Message, other.Message, StringComparison.Ordinal)
        && string.Equals(ErrorId, other.ErrorId, StringComparison.Ordinal)
        && SameEntries(Metadata, other.Metadata);

    /// <summary>Hashes the status by the same values <see cref="Equals(CompletionStatus)"/>
    /// compares, independently of the order <see cref="Metadata"/> enumerates in.</summary>
    /// <returns>The hash code of this status.</returns>
    public override int GetHashCode()
    {
        var entries = 0;
        foreach (var entry in Metadata)
        {
            entries ^= HashCode.Combine(entry.Key, entry.Value);
        }

        return HashCode.Combine(Code, Message, ErrorId, entries);
    }

    private static bool SameEntries(
        IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left.Count != right.Count)
            return false;

        var ordinalRight = new Dictionary<string, string>(right, StringComparer.Ordinal);
        foreach (var entry in left)
        {
            if (!ordinalRight.TryGetValue(entry.Key, out var value)
                || !string.Equals(entry.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// The synchronizer a completion was sequenced on, together with the record time it was
/// sequenced at.
/// </summary>
/// <param name="SynchronizerId">The id of the synchronizer that sequenced the completion.</param>
/// <param name="RecordTime">The record time the completion was sequenced at.</param>
public sealed record SynchronizerTime(string SynchronizerId, DateTimeOffset RecordTime);
