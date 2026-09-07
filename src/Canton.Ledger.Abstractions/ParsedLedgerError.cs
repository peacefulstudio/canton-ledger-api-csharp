// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Outcomes;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// A failure decoded from a Ledger API transport's error channel — the gRPC client reads it from
/// the <c>grpc-status-details-bin</c> trailer, the HTTP client from the JSON response body — in the
/// one shape both clients hand on to <see cref="ExerciseOutcome{T}"/> and
/// <see cref="Daml.Ledger.Abstractions.LedgerOperationException"/>. The two arms are what a caller
/// pattern-matches over: <see cref="Structured"/> when the participant attached a classified error
/// to its answer, <see cref="Unstructured"/> when it did not and only the transport's own status is
/// left to go on.
/// </summary>
/// <remarks>
/// The hierarchy is closed: the base constructor is private, so the only arms are the two nested
/// here and a pattern match over them is total.
/// </remarks>
public abstract record ParsedLedgerError
{
    private ParsedLedgerError(string message, int statusCode)
    {
        Message = message;
        StatusCode = statusCode;
    }

    /// <summary>The participant's error message, or the transport's own when it issued no status.</summary>
    public string Message { get; }

    /// <summary>
    /// The transport's status code for the failure — the HTTP response status for the JSON
    /// transport, the gRPC status code for gRPC. This is the value each client passes to
    /// <see cref="ExerciseOutcome{T}.InfraError"/>, never the <c>google.rpc.Status.code</c> carried
    /// inside the wire body.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// The classification a transport-failure slot can carry: the category when one was determined,
    /// and <c>null</c> when it was not. <see cref="DamlErrorCategory.Unknown"/> on
    /// <see cref="Structured"/> is a classifier that ran over a structured error and recognised
    /// nothing, which is not a category to hand a caller.
    /// </summary>
    public DamlErrorCategory? ClassifiedCategory => this switch
    {
        Structured structured =>
            structured.Category is DamlErrorCategory.Unknown ? null : structured.Category,
        Unstructured unstructured => unstructured.Category,
        _ => null,
    };

    /// <summary>
    /// The Canton error-code id a transport-failure slot can carry: the id a <see cref="Structured"/>
    /// error named, and <c>null</c> when there is none to hand on — an <see cref="Unstructured"/>
    /// failure, which has no error id by construction, and a <see cref="Structured"/> error that
    /// named an empty one. It discriminates two failures a status and a category cannot tell apart,
    /// such as the contention conditions that all arrive as one category.
    /// </summary>
    public string? ReportedErrorId => this switch
    {
        Structured { ErrorId.Length: > 0 } structured => structured.ErrorId,
        _ => null,
    };

    private const char CategoryListSeparator = ',';

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

    /// <summary>
    /// A participant error the participant classified itself: it attached a structured error to its
    /// answer, so the failure carries an error id, a category and the error's metadata alongside the
    /// transport status.
    /// </summary>
    /// <param name="Category">
    /// The Canton error category, classified by <see cref="MapCategory"/> from the <c>category</c>
    /// entry of the error's metadata; <see cref="DamlErrorCategory.Unknown"/> when a classifier ran
    /// over the structured error and found nothing it recognised. Absence of a classification is
    /// <see cref="Unstructured"/>, never <see cref="DamlErrorCategory.Unknown"/> here.
    /// </param>
    /// <param name="ErrorId">
    /// The Canton error-code id — the <c>reason</c> of the <c>google.rpc.ErrorInfo</c> detail, or
    /// the JSON envelope's <c>code</c> — e.g. <c>CONTRACT_NOT_FOUND</c>.
    /// </param>
    /// <param name="Message">The participant's error message.</param>
    /// <param name="Metadata">The error's metadata verbatim, including the raw <c>category</c> entry.</param>
    /// <param name="StatusCode">The transport's status code for the failure.</param>
    public sealed record Structured(
        DamlErrorCategory Category,
        string ErrorId,
        string Message,
        IReadOnlyDictionary<string, string> Metadata,
        int StatusCode) : ParsedLedgerError(Message, StatusCode);

    /// <summary>
    /// A failure the participant attached no structured error to — a transport fault, a body the
    /// client could not read as one, or a redacted error whose classification the participant
    /// withheld on purpose. There is no error id and no metadata to be had; the transport status is
    /// the whole of what arrived, and <see cref="Category"/> carries the coarse class a client
    /// recovered from that status when one is determinate.
    /// </summary>
    public sealed record Unstructured : ParsedLedgerError
    {
        /// <summary>
        /// Creates an unstructured failure carrying the transport's own status and message.
        /// </summary>
        /// <param name="message">The failure's message; a null message becomes an empty one.</param>
        /// <param name="statusCode">The transport's status code for the failure.</param>
        /// <param name="category">
        /// The class recovered from <paramref name="statusCode"/>, or <c>null</c> when the failure
        /// was not classified.
        /// </param>
        public Unstructured(string? message, int statusCode, DamlErrorCategory? category = null)
            : base(message ?? string.Empty, statusCode) => Category = category;

        /// <summary>
        /// The class a client recovered from the transport status alone, and <c>null</c> when the
        /// failure was not classified — the same contract the transport-failure slots upstream
        /// declare.
        /// </summary>
        public DamlErrorCategory? Category { get; init; }
    }
}
