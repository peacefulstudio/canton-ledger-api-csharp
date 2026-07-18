// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using Refit;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// The <see cref="RefitSettings"/> every Canton JSON Ledger API interface is built with —
/// both the generated per-service interfaces from <c>Canton.Ledger.Rest</c> and the
/// hand-authored off-spec ones in this package share the same serializer configuration.
/// </summary>
public static class RestRefitSettings
{
    /// <summary>
    /// System.Text.Json options consistent with the generated contracts: property names come
    /// from the generated <see cref="JsonPropertyNameAttribute"/> metadata (proto field names),
    /// and unset optional fields are omitted on the wire as proto3 JSON does.
    /// </summary>
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Creates the settings used by the DI registrations when building interface instances
    /// via <see cref="RestService"/>. Use this when hand-building an instance outside the
    /// container so its wire behavior matches the registered ones.
    /// </summary>
    public static RefitSettings Create() => new()
    {
        ContentSerializer = new SystemTextJsonContentSerializer(SerializerOptions),
        UrlParameterFormatter = new SimpleStylePathParameterFormatter(),
    };
}
