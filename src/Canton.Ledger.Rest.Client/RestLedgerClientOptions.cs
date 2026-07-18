// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Configuration options for the JSON Ledger API client, mirroring the gRPC side's
/// <c>LedgerClientOptions</c> for the REST transport.
/// </summary>
public class RestLedgerClientOptions
{
    /// <summary>
    /// The JSON Ledger API base address (e.g., "http://localhost:7575").
    /// </summary>
    [Required]
    public required string HttpAddress { get; set; }
}
