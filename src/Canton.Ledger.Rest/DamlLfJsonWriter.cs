// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json;
using WireEnum = Canton.Ledger.Rest.Client.Raw.Enum;
using WireList = Canton.Ledger.Rest.Client.Raw.List;
using WireOptional = Canton.Ledger.Rest.Client.Raw.Optional;
using WireRecord = Canton.Ledger.Rest.Client.Raw.Record;
using WireTextMap = Canton.Ledger.Rest.Client.Raw.TextMap;
using WireValue = Canton.Ledger.Rest.Client.Raw.Value;
using WireVariant = Canton.Ledger.Rest.Client.Raw.Variant;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Writes the generated wire <see cref="Raw.Value"/> and <see cref="Raw.Record"/> shapes as Daml-LF
/// JSON, the encoding the Canton JSON Ledger API expects for contract payloads and choice arguments.
/// </summary>
/// <remarks>
/// Not retired by digital-asset/canton#527. Daml-LF JSON's shape depends on the template's Daml
/// type, so no OpenAPI schema can express it and this transformation is irreducible.
/// </remarks>
internal static class DamlLfJsonWriter
{
    private const string VariantTagPropertyName = "tag";
    private const string VariantValuePropertyName = "value";
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.FFFFFFFZ";
    private const long TicksPerMicrosecond = 10;

    private static readonly DateOnly Epoch = new(1970, 1, 1);

    /// <summary>Writes <paramref name="record"/> as a Daml-LF JSON object keyed by field label.</summary>
    public static void WriteRecord(Utf8JsonWriter writer, WireRecord record)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(record);

        writer.WriteStartObject();
        foreach (var field in record.Fields ?? [])
        {
            if (field is null)
                throw new JsonException("A Daml-LF JSON record cannot contain a null field.");

            if (string.IsNullOrEmpty(field.Label))
                throw new JsonException("A Daml-LF JSON record requires a label on every field, but one field had none.");

            if (field.Value is null)
                throw new JsonException($"Record field '{field.Label}' reached the wire with no value set.");

            writer.WritePropertyName(field.Label);
            WriteValue(writer, field.Value);
        }

        writer.WriteEndObject();
    }

    /// <summary>Writes <paramref name="value"/> in the Daml-LF JSON encoding.</summary>
    public static void WriteValue(Utf8JsonWriter writer, WireValue value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        if (value.Record is not null) { WriteRecord(writer, value.Record); return; }
        if (value.Bool is { } boolean) { writer.WriteBooleanValue(boolean); return; }
        if (value.Int64 is not null) { writer.WriteStringValue(value.Int64); return; }
        if (value.Numeric is not null) { writer.WriteStringValue(value.Numeric); return; }
        if (value.Text is not null) { writer.WriteStringValue(value.Text); return; }
        if (value.Party is not null) { writer.WriteStringValue(value.Party); return; }
        if (value.ContractId is not null) { writer.WriteStringValue(value.ContractId); return; }
        if (value.Date is { } days) { writer.WriteStringValue(FormatDate(days)); return; }
        if (value.Timestamp is not null) { writer.WriteStringValue(FormatTimestamp(value.Timestamp)); return; }
        if (value.AdditionalProperties.ContainsKey(WireValueNames.Unit)) { WriteUnit(writer); return; }
        if (value.Optional is not null) { WriteOptional(writer, value.Optional); return; }
        if (value.List is not null) { WriteList(writer, value.List); return; }
        if (value.TextMap is not null) { WriteTextMap(writer, value.TextMap); return; }
        if (value.Variant is not null) { WriteVariant(writer, value.Variant); return; }
        if (value.Enum is not null) { WriteEnum(writer, value.Enum); return; }
        if (value.GenMap is not null)
            throw new JsonException("Daml-LF JSON encoding of GenMap is not implemented; no supported template uses one yet.");

        throw new JsonException("A Daml value reached the wire with no arm set, so it cannot be encoded as Daml-LF JSON.");
    }

    private static void WriteUnit(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteEndObject();
    }

    private static void WriteOptional(Utf8JsonWriter writer, WireOptional optional)
    {
        var inner = optional.Value;
        if (inner is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (inner.Optional is not null)
            throw new JsonException(
                "Daml-LF JSON encodes a nested optional in an array form that is not implemented; no supported template uses one yet.");

        WriteValue(writer, inner);
    }

    private static void WriteList(Utf8JsonWriter writer, WireList list)
    {
        writer.WriteStartArray();
        foreach (var element in list.Elements ?? [])
        {
            if (element is null)
                throw new JsonException("A Daml-LF JSON list cannot contain a null element.");

            WriteValue(writer, element);
        }

        writer.WriteEndArray();
    }

    private static void WriteTextMap(Utf8JsonWriter writer, WireTextMap textMap)
    {
        writer.WriteStartObject();
        foreach (var entry in textMap.Entries ?? [])
        {
            if (entry is null)
                throw new JsonException("A Daml-LF JSON text map cannot contain a null entry.");

            if (string.IsNullOrEmpty(entry.Key))
                throw new JsonException("A Daml-LF JSON text map requires a key on every entry, but one entry had none.");

            if (entry.Value is null)
                throw new JsonException($"Text map entry '{entry.Key}' reached the wire with no value set.");

            writer.WritePropertyName(entry.Key);
            WriteValue(writer, entry.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteVariant(Utf8JsonWriter writer, WireVariant variant)
    {
        if (string.IsNullOrEmpty(variant.Constructor))
            throw new JsonException("A Daml-LF JSON variant requires a constructor, but one had none.");

        if (variant.Value is null)
            throw new JsonException($"Variant '{variant.Constructor}' reached the wire with no value set.");

        writer.WriteStartObject();
        writer.WriteString(VariantTagPropertyName, variant.Constructor);
        writer.WritePropertyName(VariantValuePropertyName);
        WriteValue(writer, variant.Value);
        writer.WriteEndObject();
    }

    private static void WriteEnum(Utf8JsonWriter writer, WireEnum wireEnum)
    {
        if (string.IsNullOrEmpty(wireEnum.Constructor))
            throw new JsonException("A Daml-LF JSON enum requires a constructor, but one had none.");

        writer.WriteStringValue(wireEnum.Constructor);
    }

    private static string FormatDate(int daysSinceEpoch)
    {
        try
        {
            return Epoch.AddDays(daysSinceEpoch).ToString(DateFormat, CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException outOfRange)
        {
            throw new JsonException(
                $"A Daml date of {daysSinceEpoch} days since the epoch is outside the range Daml-LF JSON can express.",
                outOfRange);
        }
    }

    private static string FormatTimestamp(string microsecondsSinceEpoch)
    {
        if (!long.TryParse(microsecondsSinceEpoch, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
            throw new JsonException(
                $"A Daml timestamp is microseconds since the epoch, but '{microsecondsSinceEpoch}' is not a whole number.");

        try
        {
            return DateTimeOffset.UnixEpoch
                .AddTicks(checked(microseconds * TicksPerMicrosecond))
                .UtcDateTime
                .ToString(TimestampFormat, CultureInfo.InvariantCulture);
        }
        catch (Exception outOfRange) when (outOfRange is ArgumentOutOfRangeException or OverflowException)
        {
            throw new JsonException(
                $"A Daml timestamp of {microseconds} microseconds since the epoch is outside the range Daml-LF JSON can express.",
                outOfRange);
        }
    }
}
