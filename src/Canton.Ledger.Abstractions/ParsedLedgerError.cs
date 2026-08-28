// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Outcomes;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// A participant error decoded from the <c>google.rpc.Status</c> payload every Ledger API
/// transport returns — the gRPC client reads it from the <c>grpc-status-details-bin</c> trailer,
/// the HTTP client from the JSON response body — in the one shape both clients hand on to
/// <see cref="ExerciseOutcome{T}"/> and <see cref="Daml.Ledger.Abstractions.LedgerOperationException"/>.
/// </summary>
/// <param name="Category">
/// The Canton error category, classified by <see cref="MapCategory"/> from the
/// <c>category</c> entry of the error's <c>google.rpc.ErrorInfo</c> metadata;
/// <see cref="DamlErrorCategory.Unknown"/> when the payload carries no recognisable category.
/// </param>
/// <param name="ErrorId">
/// The Canton error-code id (the <c>reason</c> of the <c>google.rpc.ErrorInfo</c> detail), e.g.
/// <c>CONTRACT_NOT_FOUND</c>. Empty when the payload carries no <c>ErrorInfo</c> detail, which is
/// how both clients tell a participant-issued Daml error from a bare transport failure.
/// </param>
/// <param name="Message">The participant's error message, or the transport's own when it issued no status.</param>
/// <param name="Metadata">The <c>google.rpc.ErrorInfo</c> metadata verbatim, including the raw <c>category</c> entry.</param>
/// <param name="StatusCode">
/// The transport's status code for the failure — the HTTP response status for the JSON transport,
/// the gRPC status code for gRPC. This is the value each client passes to
/// <see cref="ExerciseOutcome{T}.InfraError"/>, never the <c>google.rpc.Status.code</c> carried
/// inside the wire body.
/// </param>
public sealed record ParsedLedgerError(
    DamlErrorCategory Category,
    string ErrorId,
    string Message,
    IReadOnlyDictionary<string, string> Metadata,
    int StatusCode)
{
    private const char CategoryListSeparator = ',';

    private static readonly IReadOnlyDictionary<string, string> NoMetadata =
        new Dictionary<string, string>(0);

    /// <summary>
    /// A failure carrying no participant <c>ErrorInfo</c> — an empty <see cref="ErrorId"/>,
    /// <see cref="DamlErrorCategory.Unknown"/>, and no metadata — so callers surface it as a
    /// transport failure rather than a Daml error.
    /// </summary>
    public static ParsedLedgerError Untyped(string? message, int statusCode) =>
        new(DamlErrorCategory.Unknown, string.Empty, message ?? string.Empty, NoMetadata, statusCode);

    /// <summary>
    /// Classifies a Canton error category as it arrives on the wire, whichever field carried it —
    /// today the <c>category</c> entry of a participant error's <c>google.rpc.ErrorInfo</c>
    /// metadata. Participants send the category's <em>numeric id</em> (<c>"8"</c>), and
    /// <see cref="DamlErrorCategory"/>'s members are numbered to match Canton's documented ids, so
    /// 1–14 map to their category; the category name is accepted too, case-insensitively. Anything
    /// else — absent, blank, an out-of-range id, an unrecognised name, or a comma-separated list —
    /// is <see cref="DamlErrorCategory.Unknown"/>. A list is rejected before parsing because
    /// <c>Enum.TryParse</c> combines its members bitwise even though <see cref="DamlErrorCategory"/>
    /// carries no <see cref="FlagsAttribute"/>, landing on an unrelated third category that
    /// <c>Enum.IsDefined</c> then accepts.
    /// </summary>
    public static DamlErrorCategory MapCategory(string? wireCategory) =>
        !string.IsNullOrWhiteSpace(wireCategory)
        && !wireCategory.Contains(CategoryListSeparator)
        && Enum.TryParse<DamlErrorCategory>(wireCategory, ignoreCase: true, out var category)
        && Enum.IsDefined(category)
            ? category
            : DamlErrorCategory.Unknown;
}
