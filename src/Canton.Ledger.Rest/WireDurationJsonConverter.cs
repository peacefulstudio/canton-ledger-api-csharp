// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Adapts a transport duration that our specification declares as the proto3-canonical string to the
/// <c>{"seconds":…,"nanos":…}</c> object the Canton JSON Ledger API serves and accepts. Reading
/// tolerates both forms; writing always emits the object, because the participant drops a duration
/// key whose value is anything else — measured against Canton 3.5.9, a command carrying
/// <c>"minLedgerTimeRel":"3600s"</c> commits immediately with no bound and no error.
/// <para>
/// A value that is not a proto3-canonical duration is refused here rather than sent. The participant
/// accepts and discards such a value, so passing it through would put the caller back in the silence
/// this converter exists to end.
/// </para>
/// <para>
/// This converter is attached to individual properties through
/// <see cref="WireDurationSites.UseWireEncoding"/> and must never be registered globally: it would
/// reshape every string on the wire, including the Daml <c>Text</c> values inside a contract payload.
/// </para>
/// </summary>
/// <remarks>
/// Retired by digital-asset/canton#527, which would have the JSON HTTP API generated from the
/// annotated protobuf definitions this duration shape comes from.
/// </remarks>
internal sealed class WireDurationJsonConverter : JsonConverter<string>
{
    internal static readonly WireDurationJsonConverter Instance = new();

    /// <inheritdoc />
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return null;
                case JsonTokenType.String:
                    return reader.GetString();
                default:
                    using (var document = JsonDocument.ParseValue(ref reader))
                    {
                        return document.RootElement.ValueKind == JsonValueKind.Object
                            ? WireDuration.CanonicalOf(document.RootElement)
                            : throw WireDuration.Malformed($"a JSON {document.RootElement.ValueKind} value");
                    }
            }
        }
        catch (Exception failure) when (failure is not JsonException)
        {
            throw WireDuration.Malformed("a value that could not be read", failure);
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        var (seconds, nanos) = WireDuration.PartsOf(value);
        writer.WriteStartObject();
        writer.WriteNumber(WireDuration.SecondsProperty, seconds);
        writer.WriteNumber(WireDuration.NanosProperty, nanos);
        writer.WriteEndObject();
    }
}
