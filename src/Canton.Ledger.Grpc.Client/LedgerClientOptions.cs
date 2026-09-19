// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using Canton.Ledger.Kernel.Resilience;
using Canton.Ledger.Kernel.Security;
using Grpc.Net.Client;

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// Configuration options for the Ledger API client.
/// </summary>
public class LedgerClientOptions : IValidatableObject
{
    /// <summary>
    /// The gRPC endpoint address (e.g., "https://localhost:5001"). An <c>http</c> address opens a
    /// cleartext channel: when a token-issuing
    /// <see cref="Canton.Ledger.Abstractions.ITokenProvider"/> is configured, bearer
    /// tokens are sent in cleartext, readable by anyone on the network path, and the client
    /// logs a warning at construction. Use <c>https</c> for anything beyond local development.
    /// </summary>
    [Required]
    public required string GrpcAddress { get; set; }

    /// <summary>
    /// The user ID for command submissions (Ledger API v2).
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Maximum message size in bytes. Default is 100MB.
    /// </summary>
    public int MaxMessageSize { get; set; } = 100 * 1024 * 1024;

    /// <summary>
    /// Interval between HTTP/2 keep-alive pings sent on the gRPC connection so a silently dropped
    /// connection behind a NAT or L4 load balancer surfaces promptly instead of leaving a long-running
    /// <c>SubscribeAsync</c>/<c>CompletionStreamAsync</c> reader hung forever. Default is 60 seconds.
    /// </summary>
    public TimeSpan KeepAlivePingDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long a keep-alive ping waits for its acknowledgement before the connection is treated as
    /// dead and the stream fails. Default is 20 seconds.
    /// </summary>
    public TimeSpan KeepAlivePingTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Optional hook to tune the <see cref="GrpcChannelOptions"/> the client builds — for example to
    /// adjust the <see cref="System.Net.Http.SocketsHttpHandler"/> it already created, or to set a
    /// channel option the SDK does not expose. It runs after the SDK applies its defaults (message
    /// sizes and keep-alive), so anything it sets wins over them, up to and including replacing
    /// <see cref="GrpcChannelOptions.HttpHandler"/> outright — see the remarks before doing so. That
    /// includes <see cref="Tls"/>: the typed TLS material is on the handler before this hook runs, so
    /// a hook that assigns <see cref="System.Net.Http.SocketsHttpHandler.SslOptions"/> replaces it.
    /// </summary>
    /// <remarks>
    /// Microsoft's own gRPC client-certificate sample assigns an
    /// <see cref="System.Net.Http.HttpClientHandler"/> to
    /// <see cref="GrpcChannelOptions.HttpHandler"/>, replacing the handler this client built rather
    /// than adjusting it. A hook copied from that sample therefore drops the keep-alive settings
    /// <see cref="KeepAlivePingDelay"/> and <see cref="KeepAlivePingTimeout"/> configure alongside the
    /// TLS material, and a silently dropped keep-alive surfaces only as a long-running stream hanging
    /// on a connection that died behind a NAT. Configure the handler this client built —
    /// <c>(SocketsHttpHandler)options.HttpHandler!</c> — rather than assigning a new one.
    /// </remarks>
    public Action<GrpcChannelOptions>? ConfigureChannel { get; set; }

    /// <summary>
    /// Optional timeout for gRPC calls.
    /// </summary>
    /// <remarks>
    /// This is a <em>per-attempt</em> deadline: when <see cref="Retry"/> is enabled, every retry
    /// attempt is granted a fresh <see cref="Timeout"/> budget rather than sharing one budget across
    /// the whole retry sequence. The overall wall-clock ceiling on a retried call is therefore the
    /// caller's <see cref="System.Threading.CancellationToken"/> (which halts retries promptly) plus
    /// the finite attempt count and exponential backoff — not this single deadline.
    /// </remarks>
    public TimeSpan? Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The opt-in retry pipeline applied to each unary RPC. Disabled by default, so the
    /// client's transport behavior is unchanged unless a consumer explicitly opts in. Only transient
    /// transport failures (gRPC <c>Unavailable</c> / <c>DeadlineExceeded</c>) are retried; Daml
    /// business errors and non-transient status codes are surfaced without retry. Retries reuse the
    /// stable <c>command_id</c> fixed above the retry boundary, so ledger-side deduplication makes a
    /// resubmission idempotent — the pipeline itself confers no idempotency.
    /// </summary>
    /// <remarks>
    /// A first attempt can commit while its response is lost; the participant then rejects the
    /// resubmission with <c>DUPLICATE_COMMAND</c> even though the intended ledger change succeeded.
    /// On the <c>Try*</c> submission paths the client maps that rejection back to success: it reads
    /// the <c>completion_offset</c> from the rejection's error metadata, point-reads the committed
    /// transaction via <see cref="LedgerClient.GetUpdateByOffsetAsync"/>, and surfaces it as the
    /// success outcome. The mapping applies only when this pipeline actually retried the submission —
    /// a first-attempt <c>DUPLICATE_COMMAND</c> from a caller-chosen <c>command_id</c> is a genuine
    /// caller error and stays a <c>DamlError</c>, as does a retried rejection whose metadata carries
    /// no usable <c>completion_offset</c> or whose point read fails.
    /// </remarks>
    public RetryOptions Retry { get; set; } = new();

    /// <summary>
    /// The TLS material this client presents as its own identity and accepts as trust anchors.
    /// Unconfigured by default, so the channel negotiates TLS exactly as it did before — server
    /// authentication against the operating system trust store, no client certificate — and nothing
    /// is applied to the handler at all. Configuring it projects the material onto the
    /// <see cref="System.Net.Http.SocketsHttpHandler.SslOptions"/> of the handler the client builds,
    /// which is what a Canton participant demanding mutual TLS requires.
    /// </summary>
    /// <remarks>
    /// This is applied before <see cref="ConfigureChannel"/> runs, so a host that sets
    /// <see cref="System.Net.Http.SocketsHttpHandler.SslOptions"/> from that hook — or replaces the
    /// handler outright — overrides everything configured here.
    /// </remarks>
    public TlsOptions Tls { get; set; } = new();

    /// <summary>
    /// Recurses into <see cref="Retry"/> and <see cref="Tls"/> so their validation runs under the same
    /// <c>ValidateDataAnnotations().ValidateOnStart()</c> pipeline as this type — runtime
    /// data-annotation validation does not descend into nested options on its own — surfacing a
    /// misconfigured retry pipeline or an incoherent TLS combination at startup rather than at the
    /// first RPC or the first handshake.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var nestedResults = new List<ValidationResult>();
        Validator.TryValidateObject(
            Retry, new ValidationContext(Retry), nestedResults, validateAllProperties: true);
        Validator.TryValidateObject(
            Tls, new ValidationContext(Tls), nestedResults, validateAllProperties: true);
        return nestedResults;
    }
}
