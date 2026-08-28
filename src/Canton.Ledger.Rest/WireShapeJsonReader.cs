// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Deserializes a payload into the wire shape our own specification declares, with the Daml-LF JSON
/// converters removed so the Daml-LF writers stay a write-path concern and never re-enter on a read.
/// </summary>
internal static class WireShapeJsonReader
{
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> Relaxed = new();

    public static TWire? Read<TWire>(ref Utf8JsonReader reader, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<TWire>(ref reader, Relaxed.GetValue(options, WithoutDamlLfConverters));

    private static JsonSerializerOptions WithoutDamlLfConverters(JsonSerializerOptions options)
    {
        var relaxed = new JsonSerializerOptions(options);
        for (var index = relaxed.Converters.Count - 1; index >= 0; index--)
        {
            if (relaxed.Converters[index] is WireValueJsonConverter or WireRecordJsonConverter)
                relaxed.Converters.RemoveAt(index);
        }

        return relaxed;
    }
}
