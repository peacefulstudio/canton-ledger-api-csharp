// Copyright © 2026 Peaceful Studio OÜ. All rights reserved.

using System.ComponentModel.DataAnnotations;

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
    public TimeSpan? Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
