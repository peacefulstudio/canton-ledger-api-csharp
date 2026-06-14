// Copyright 2026 Peaceful Studio OÜ

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
    /// When <c>null</c>, the client uses <see cref="PqsClient.DefaultJsonSerializerOptions"/>.
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }
}
