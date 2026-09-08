// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    /// <remarks>
    /// Setting this replaces those defaults rather than adding to them, so payloads needing both
    /// them and a converter of your own start from
    /// <see cref="CreateDefaultJsonSerializerOptions"/> and add the converter to what it returns.
    /// </remarks>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    /// <summary>
    /// Builds a fresh, mutable <see cref="System.Text.Json.JsonSerializerOptions"/> carrying the
    /// defaults the client applies when <see cref="JsonSerializerOptions"/> is <c>null</c>:
    /// <see cref="System.Text.Json.JsonSerializerOptions.PropertyNameCaseInsensitive"/> for PQS's
    /// camelCase keys, <see cref="JsonNumberHandling.AllowReadingFromString"/> for Daml
    /// <c>Numeric</c>, and a <see cref="JsonStringEnumConverter"/> for Daml enums.
    /// </summary>
    /// <returns>
    /// A new instance on every call, so a converter added to one — a variant factory for an
    /// abstract Daml type System.Text.Json cannot construct, say — reaches only the client it is
    /// assigned to.
    /// </returns>
    public static JsonSerializerOptions CreateDefaultJsonSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
