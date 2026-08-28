// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using Canton.Ledger.Kernel.Resilience;

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
    /// Caps how many entries one <c>CompletionStreamAsync</c> window returns, sent as the
    /// <c>limit</c> query parameter on <c>POST /v2/commands/completions</c>. Left unset, the
    /// participant applies its own <c>http-list-max-elements-limit</c>.
    /// </summary>
    public long? CompletionStreamLimit { get; set; }

    /// <summary>
    /// How long the participant holds a <c>CompletionStreamAsync</c> window open once no further
    /// completion arrives, sent as the <c>stream_idle_timeout_ms</c> query parameter on
    /// <c>POST /v2/commands/completions</c> and rounded down to whole milliseconds. Left unset, the
    /// participant applies its own idle timeout. This bounds how long a single call blocks, so a
    /// caller following the stream trades window latency against request volume here.
    /// </summary>
    public TimeSpan? CompletionStreamIdleTimeout { get; set; }

    /// <summary>
    /// Recurses into <see cref="Retry"/> so its validation runs under the same
    /// <c>ValidateDataAnnotations().ValidateOnStart()</c> pipeline as this type — runtime
    /// data-annotation validation does not descend into nested options on its own — surfacing a
    /// misconfigured retry pipeline at startup rather than at the first request.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            Retry, new ValidationContext(Retry), results, validateAllProperties: true);

        if (CompletionStreamLimit is { } limit && limit <= 0)
        {
            results.Add(new ValidationResult(
                $"{nameof(CompletionStreamLimit)} must be positive when set, but was {limit}.",
                [nameof(CompletionStreamLimit)]));
        }

        if (CompletionStreamIdleTimeout is { } idleTimeout && idleTimeout <= TimeSpan.Zero)
        {
            results.Add(new ValidationResult(
                $"{nameof(CompletionStreamIdleTimeout)} must be positive when set, but was {idleTimeout}.",
                [nameof(CompletionStreamIdleTimeout)]));
        }

        return results;
    }
}
