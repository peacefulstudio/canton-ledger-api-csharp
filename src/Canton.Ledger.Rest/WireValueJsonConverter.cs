// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using Canton.Ledger.Rest.Client.Raw;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Serializes the generated wire <see cref="Value"/> as Daml-LF JSON on the command submission path.
/// Reading is left as our specification declares it, because decoding Daml-LF JSON back into an arm
/// needs the template's Daml type — <c>{"owner":"alice::1220ab"}</c> does not say whether
/// <c>owner</c> is a <c>Party</c> or a <c>Text</c>.
/// </summary>
/// <remarks>
/// Not retired by digital-asset/canton#527; the Daml-LF JSON encoding is type-directed and cannot be
/// expressed in any OpenAPI schema. Deserialization arrives with the read path.
/// </remarks>
internal sealed class WireValueJsonConverter : JsonConverter<Value>
{
    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override Value Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        WireShapeJsonReader.Read<Value>(ref reader, options)!;

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
