// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using Canton.Ledger.Rest.Client.Raw;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Serializes the generated wire <see cref="Record"/> as Daml-LF JSON on the command submission path,
/// the shape contract payloads and record-valued choice arguments take, and keeps a read record's raw
/// Daml-LF JSON text under <see cref="WireValueNames.Idiomatic"/> without binding
/// <see cref="Record.Fields"/> or <see cref="Record.RecordId"/>. A participant only ever sends
/// Daml-LF JSON, and decoding it back into labelled fields needs the template's Daml type —
/// <c>{"owner":"alice::1220ab"}</c> does not say whether <c>owner</c> is a <c>Party</c> or a
/// <c>Text</c>, and <c>{"fields":"hello"}</c> is a record with a field named <c>fields</c>.
/// </summary>
/// <remarks>
/// Not retired by digital-asset/canton#527; the Daml-LF JSON encoding is type-directed and cannot be
/// expressed in any OpenAPI schema.
/// </remarks>
internal sealed class WireRecordJsonConverter : JsonConverter<Record>
{
    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override Record Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null) return null!;

        using var idiomaticRecord = JsonDocument.ParseValue(ref reader);
        var record = new Record();
        record.AdditionalProperties[WireValueNames.Idiomatic] = idiomaticRecord.RootElement.GetRawText();
        return record;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Record value, JsonSerializerOptions options)
    {
        if (value is null)
            throw new JsonException("A Daml record reached the wire as null and cannot be encoded as Daml-LF JSON.");

        try
        {
            DamlLfJsonWriter.WriteRecord(writer, value);
        }
        catch (Exception failure) when (failure is not JsonException)
        {
            throw new JsonException("A Daml record could not be encoded as Daml-LF JSON.", failure);
        }
    }
}
