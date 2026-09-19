// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using Canton.Ledger.Kernel.Resilience;
using Canton.Ledger.Kernel.Security;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Configuration options for the JSON Ledger API client, mirroring the gRPC side's
/// <c>LedgerClientOptions</c> for the REST transport.
/// </summary>
public class RestLedgerClientOptions : IValidatableObject
{
    /// <summary>
    /// The JSON Ledger API base address (e.g., "http://localhost:7575").
    /// </summary>
    [Required]
    public required string HttpAddress { get; set; }

    /// <summary>
    /// The user ID for command submissions (Ledger API v2). Optional: when omitted, the
    /// participant derives it from the caller's access token.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// The opt-in retry pipeline applied to each HTTP request. Disabled by default, so the client's
    /// transport behavior is unchanged unless a consumer explicitly opts in. Only transient
    /// transport failures are retried — a refused, reset, or DNS-failed connection
    /// (<see cref="HttpRequestException"/>) and a client-side request timeout, the HTTP analogues of
    /// the gRPC client's <c>Unavailable</c>/<c>DeadlineExceeded</c>. Retries reuse the stable
    /// <c>command_id</c> fixed above the retry boundary, so ledger-side deduplication makes a
    /// resubmission idempotent — the pipeline itself confers no idempotency.
    /// </summary>
    /// <remarks>
    /// Two asymmetries against the gRPC pipeline are deliberate and consumer-visible. A participant
    /// that answers with a status code — 429, 503, a gateway 5xx — is <em>not</em> retried: on HTTP
    /// that is a response rather than an exception, and the kernel's transient-failure predicate
    /// classifies exceptions only. And where the gRPC client maps a retried
    /// <c>DUPLICATE_COMMAND</c> rejection back to success by point-reading the committed
    /// transaction from the rejection's <c>completion_offset</c>, the JSON API does not serve that
    /// metadata, so a first attempt that commits while its response is lost surfaces the
    /// resubmission's <c>DUPLICATE_COMMAND</c> to the caller even though the ledger change
    /// succeeded.
    /// <para>
    /// Enabling retries also makes each request body buffered in memory before the first attempt,
    /// so it can be replayed on the next one. Command submissions are small enough for this to be
    /// irrelevant, but a caller pushing very large bodies through this client — a DAR upload, say —
    /// should weigh that cost before opting in.
    /// </para>
    /// </remarks>
    public RetryOptions Retry { get; set; } = new();

    /// <summary>
    /// The TLS material the client presents as its own identity and accepts as trust anchors,
    /// applied to the <see cref="HttpClient"/> named
    /// <see cref="ServiceCollectionExtensions.HttpClientName"/>. Unconfigured by default, so the
    /// client validates the participant against the operating system trust store and presents no
    /// client certificate unless a consumer opts in.
    /// </summary>
    /// <remarks>
    /// A host that supplies a primary handler of its own for the named client — through
    /// <c>services.AddHttpClient(ServiceCollectionExtensions.HttpClientName)</c> and one of the
    /// <c>ConfigurePrimaryHttpMessageHandler</c> overloads that hand back a handler — overrides
    /// anything set here. Configuring that same named client for an unrelated concern through an
    /// overload that adjusts the handler already in place, such as <c>UseSocketsHttpHandler</c>,
    /// keeps it. See <see cref="ServiceCollectionExtensions.HttpClientName"/> for what that costs
    /// and where the guarantee stops.
    /// </remarks>
    public TlsOptions Tls { get; set; } = new();

    /// <summary>
    /// The <see cref="StreamWindowLimit"/> a client applies when a consumer configures none:
    /// <c>200</c>, the value Canton documents as the default of a participant's
    /// <c>http-list-max-elements-limit</c>.
    /// </summary>
    public const long DefaultStreamWindowLimit = 200L;

    /// <summary>
    /// The <see cref="StreamWindowIdleTimeout"/> a client applies when a consumer configures none:
    /// two seconds, the <c>http-list-wait-time</c> value Canton's own documentation uses in its
    /// worked example.
    /// </summary>
    public static readonly TimeSpan DefaultStreamWindowIdleTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Caps how many entries one window of a looped read returns, sent as the <c>limit</c> query
    /// parameter on every window the pagination loop opens — <c>POST /v2/updates</c> and
    /// <c>POST /v2/commands/completions</c>. Defaults to <see cref="DefaultStreamWindowLimit"/>.
    /// </summary>
    /// <remarks>
    /// Canton documents that an explicit <c>limit</c> at or below the participant's
    /// <c>http-list-max-elements-limit</c> never produces the <c>413 Content Too Large</c> that an
    /// unbounded request can, and that the participant's own cap defaults to 200 — so the default
    /// here rides a default-configured participant without tripping it. A participant configured
    /// below this value answers the first window with a 413, which reaches the caller as a terminal
    /// in-band stream error naming this option; lower it to match that participant.
    /// <para>
    /// The ACS snapshot is deliberately not bounded by this. It is a single read the client does
    /// not page, so capping it would hand a caller a short snapshot that looks complete, where
    /// leaving the participant's own cap to answer makes an oversized snapshot a loud failure.
    /// </para>
    /// </remarks>
    public long StreamWindowLimit { get; set; } = DefaultStreamWindowLimit;

    /// <summary>
    /// How long the participant holds one window open once no further entry arrives, sent as the
    /// <c>stream_idle_timeout_ms</c> query parameter on every window the pagination loop opens and
    /// rounded down to whole milliseconds. Defaults to
    /// <see cref="DefaultStreamWindowIdleTimeout"/>.
    /// </summary>
    /// <remarks>
    /// Canton's documentation states that a window carrying neither an end offset nor this timeout
    /// never closes, so the client always sends it; two seconds is the <c>http-list-wait-time</c>
    /// its worked example uses. This bounds how long a single request blocks on a quiet ledger, so
    /// a consumer following a stream trades window latency against request volume here.
    /// <para>
    /// That trade has a ceiling the participant imposes rather than answers: a LocalNet participant
    /// running Canton 3.5.11 abandons a request it has held for twenty seconds and replies
    /// <c>503</c> with a "not able to produce a timely response" body, which reaches the caller as a
    /// terminal in-band stream error. The number is observed on that participant, not documented by
    /// Canton and not validated here, so it is a value to measure against the deployment rather than
    /// a contract to configure against. Raising this option past a participant's own request timeout
    /// ends the stream instead of holding the window longer.
    /// </para>
    /// <para>
    /// The participant's hold is the whole of a followed stream's pacing. The pagination loop
    /// reopens a window as soon as the previous one answers, so a participant honouring this
    /// timeout sets the request rate on its own and the client adds no backoff of its own: one
    /// would tax every window that legitimately answers fast to defend against a participant
    /// ignoring a parameter sent on every request. A participant that does ignore it is reported
    /// rather than absorbed — the loop logs a warning once a hundred consecutive windows come back
    /// neither advancing the offset nor held, and again at each doubling of that run.
    /// </para>
    /// </remarks>
    public TimeSpan StreamWindowIdleTimeout { get; set; } = DefaultStreamWindowIdleTimeout;

    /// <summary>
    /// Recurses into <see cref="Retry"/> and <see cref="Tls"/> so their validation runs under the
    /// same <c>ValidateDataAnnotations().ValidateOnStart()</c> pipeline as this type — runtime
    /// data-annotation validation does not descend into nested options on its own — surfacing a
    /// misconfigured retry pipeline or an incoherent set of TLS material at startup rather than at
    /// the first request.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            Retry, new ValidationContext(Retry), results, validateAllProperties: true);
        Validator.TryValidateObject(
            Tls, new ValidationContext(Tls), results, validateAllProperties: true);

        if (StreamWindowLimit <= 0)
        {
            results.Add(new ValidationResult(
                $"{nameof(StreamWindowLimit)} must be positive, but was {StreamWindowLimit}.",
                [nameof(StreamWindowLimit)]));
        }

        if (StreamWindowIdleTimeout <= TimeSpan.Zero)
        {
            results.Add(new ValidationResult(
                $"{nameof(StreamWindowIdleTimeout)} must be positive, but was {StreamWindowIdleTimeout}.",
                [nameof(StreamWindowIdleTimeout)]));
        }

        return results;
    }
}
