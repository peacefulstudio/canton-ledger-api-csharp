// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;

namespace Canton.Ledger.Pqs.Client;

/// <summary>
/// Configuration options for the PQS client.
/// </summary>
/// <remarks>
/// <para>
/// Payloads decode through the generated Daml-LF JSON reader, strictly. PQS documents Daml
/// <c>Numeric</c> and <c>Int64</c> as JSON strings by default, and
/// <c>--target-encoding-numericasstring</c> / <c>--target-encoding-int64asstring</c> change that
/// default; a bare JSON number where an <c>Int64</c> or <c>Numeric</c> is expected is refused.
/// </para>
/// <para>
/// <c>--target-encoding-excludenulls</c> is not supported: PQS stores nullable fields as JSON nulls
/// by default, and a payload that omits an optional field's key fails to decode with an error
/// naming the field. A row over 16 MiB, 100,000 JSON nodes or nesting depth 128 fails to decode
/// like any other undecodable row.
/// </para>
/// </remarks>
public class PqsClientOptions
{
    /// <summary>
    /// PostgreSQL connection string for the PQS database.
    /// </summary>
    [Required]
    public required string ConnectionString { get; set; }
}
