// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Outcomes;

namespace Canton.Ledger.Kernel.Streams;

/// <summary>
/// The terminal fault a stream surfaces in band, shared by the gRPC and HTTP transports.
/// </summary>
/// <remarks>
/// <see cref="StatusCode"/> means one thing only: the status the transport itself reported. A
/// participant that answered successfully with a body the client could not decode reported no
/// transport failure at all, and carries <see cref="NoTransportFailure"/> rather than an invented
/// sentinel. The two are built through <see cref="FromTransport"/> and
/// <see cref="FromUndecodableBody"/> so neither can be raised without saying which it is.
/// <para>
/// <see cref="Category"/> is the transport's own error parser's classification, and <c>null</c>
/// when the parser determined none — never a category the participant did not name. Both factories
/// take the source exception explicitly rather than defaulting it, so a new fault site cannot drop a
/// caller's only diagnostic by omission.
/// </para>
/// <para>
/// <see cref="ErrorId"/> is the participant's own Canton error code, and <c>null</c> when the
/// failure carried no structured error to read one from — including every fault
/// <see cref="FromUndecodableBody"/> raises, since a participant that answered successfully
/// reported no error code at all. It is what separates two faults their status and category cannot:
/// a <c>STALE_STREAM_AUTHORIZATION</c> a reopen clears from a contention condition that will repeat
/// identically.
/// </para>
/// </remarks>
internal sealed record StreamFault
{
    internal const int NoTransportFailure = 0;

    private StreamFault(
        int statusCode,
        string message,
        DamlErrorCategory? category,
        string? errorId,
        Exception? sourceException)
    {
        ArgumentNullException.ThrowIfNull(message);
        StatusCode = statusCode;
        Message = message;
        Category = category;
        ErrorId = errorId;
        SourceException = sourceException;
    }

    /// <summary>The status the transport reported, or <see cref="NoTransportFailure"/>.</summary>
    internal int StatusCode { get; }

    /// <summary>The fault detail carried to the caller as the terminal stream event's message.</summary>
    internal string Message { get; }

    /// <summary>The parsed Canton error category, or null when the fault carried none.</summary>
    internal DamlErrorCategory? Category { get; }

    /// <summary>The participant's Canton error code, or null when the fault carried none.</summary>
    internal string? ErrorId { get; }

    /// <summary>The exception that ended the stream, or null when none was thrown.</summary>
    internal Exception? SourceException { get; }

    internal static StreamFault FromTransport(
        int statusCode,
        string message,
        DamlErrorCategory? category,
        string? errorId,
        Exception? sourceException) =>
        new(statusCode, message, category, errorId, sourceException);

    internal static StreamFault FromUndecodableBody(string message, Exception sourceException) =>
        new(NoTransportFailure, message, null, null, sourceException);
}
