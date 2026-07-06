// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using Canton.Ledger.Kernel.Resilience;

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// Configuration options for the Ledger API client.
/// </summary>
public class LedgerClientOptions
{
    /// <summary>
    /// The gRPC endpoint address (e.g., "https://localhost:5001").
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
    /// The opt-in retry pipeline applied to each unary RPC (ADR 0006). Disabled by default, so the
    /// client's transport behavior is unchanged unless a consumer explicitly opts in. Only transient
    /// transport failures (gRPC <c>Unavailable</c> / <c>DeadlineExceeded</c>) are retried; Daml
    /// business errors and non-transient status codes are surfaced without retry. Retries reuse the
    /// stable <c>command_id</c> fixed above the retry boundary, so ledger-side deduplication makes a
    /// resubmission idempotent — the pipeline itself confers no idempotency (ADR 0006).
    /// </summary>
    public RetryOptions Retry { get; set; } = new();
}
