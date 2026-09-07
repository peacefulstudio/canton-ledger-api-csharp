// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Abstractions;

/// <summary>
/// Thrown when a participant's response body cannot be read as the Ledger API describes it — a field
/// the API marks as required is absent, or a value on the wire does not decode into the type it claims.
/// </summary>
/// <remarks>
/// This says the fault is the participant's, not the caller's: the request was accepted, a body came
/// back, and the body is the part that does not hold up. Both transports throw this one type, so a
/// consumer catches it once regardless of whether the body arrived over gRPC or over the JSON Ledger
/// API, and both build the message the same way — a fixed marker, then <see cref="Detail"/>.
/// </remarks>
public sealed class MalformedResponseException : InvalidOperationException
{
    private const string MessagePrefix = "Malformed response from ledger: ";

    /// <summary>Creates an exception describing what in the participant's body could not be read.</summary>
    /// <param name="detail">What could not be read, phrased as a sentence.</param>
    public MalformedResponseException(string detail)
        : base(MessagePrefix + detail) => Detail = detail;

    /// <summary>Creates an exception describing what in the participant's body could not be read.</summary>
    /// <param name="detail">What could not be read, phrased as a sentence.</param>
    /// <param name="innerException">The failure raised while reading the body.</param>
    public MalformedResponseException(string detail, Exception innerException)
        : base(MessagePrefix + detail, innerException) => Detail = detail;

    /// <summary>
    /// What could not be read, without the marker <see cref="Exception.Message"/> opens with, so a
    /// message that quotes this one does not repeat the marker.
    /// </summary>
    public string Detail { get; }
}
