// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Canton.Ledger.Pqs.Client;

/// <summary>
/// Configuration options for the PQS client.
/// </summary>
public class PqsClientOptions
{
    /// <summary>
    /// PostgreSQL connection string for the PQS database.
    /// </summary>
    [Required]
    public required string ConnectionString { get; set; }

    /// <summary>
    /// Optional <see cref="JsonSerializerOptions"/> for deserializing PQS contract payloads.
    /// When <c>null</c>, the client uses its own defaults: case-insensitive property matching for
    /// PQS's camelCase keys, Daml <c>Numeric</c> read from a JSON string, and Daml enums read as
    /// plain strings.
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }
}
