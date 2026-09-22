// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using Canton.Ledger.Rest.Client.Raw;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Serializes the generated wire <see cref="Value"/> as Daml-LF JSON on the command submission path,
/// and keeps a read value's raw Daml-LF JSON text under <see cref="WireValueNames.Idiomatic"/> without
/// binding any arm. A participant only ever sends Daml-LF JSON, and decoding it needs the Daml type —
/// <c>{"owner":"alice::1220ab"}</c> does not say whether <c>owner</c> is a <c>Party</c> or a
/// <c>Text</c>, and <c>{"text":"hello"}</c> is a record with a field named <c>text</c>, not a
/// <c>Text</c> arm.
/// </summary>
/// <remarks>
/// Not retired by digital-asset/canton#527; the Daml-LF JSON encoding is type-directed and cannot be
/// expressed in any OpenAPI schema.
/// </remarks>
internal sealed class WireValueJsonConverter : JsonConverter<Value>
{
    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override Value Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var idiomaticValue = JsonDocument.ParseValue(ref reader);
        var value = new Value();
        value.AdditionalProperties[WireValueNames.Idiomatic] = idiomaticValue.RootElement.GetRawText();
        return value;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Value value, JsonSerializerOptions options)
    {
        if (value is null)
            throw new JsonException("A Daml value reached the wire as null and cannot be encoded as Daml-LF JSON.");

        try
        {
            DamlLfJsonWriter.WriteValue(writer, value);
        }
        catch (Exception failure) when (failure is not JsonException)
        {
            throw new JsonException("A Daml value could not be encoded as Daml-LF JSON.", failure);
        }
    }
}
