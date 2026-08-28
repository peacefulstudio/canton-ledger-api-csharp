// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using WireEnum = Canton.Ledger.Rest.Client.Raw.Enum;
using WireGenMap = Canton.Ledger.Rest.Client.Raw.GenMap;
using WireIdentifier = Canton.Ledger.Rest.Client.Raw.Identifier;
using WireList = Canton.Ledger.Rest.Client.Raw.List;
using WireOptional = Canton.Ledger.Rest.Client.Raw.Optional;
using WireRecord = Canton.Ledger.Rest.Client.Raw.Record;
using WireTextMap = Canton.Ledger.Rest.Client.Raw.TextMap;
using WireValue = Canton.Ledger.Rest.Client.Raw.Value;
using WireVariant = Canton.Ledger.Rest.Client.Raw.Variant;

namespace Canton.Ledger.Rest.Client;

internal static class RestValueDecoder
{
    private static InvalidOperationException MalformedResponse(string detail) =>
        new($"{RestTransactionResultProjector.MalformedResponsePrefix}{detail}");

    public static DamlRecord ToDamlRecord(WireRecord? record)
    {
        if (record is null) return new DamlRecord(null, []);

        if (record.Fields is { Count: > 0 } fields)
        {
            var damlFields = new List<DamlField>(fields.Count);
            foreach (var field in fields)
            {
                var fieldValue = field?.Value
                    ?? throw MalformedResponse($"Record field '{field?.Label}' has no value set.");
                damlFields.Add(new DamlField(field.Label, ToDamlValue(fieldValue)));
            }
            return new DamlRecord(ToRuntimeIdentifier(record.RecordId), damlFields);
        }

        if (record.AdditionalProperties is { Count: > 0 } idiomaticFields)
        {
            return DamlJsonSerializer.DeserializeRecord(JsonSerializer.Serialize(idiomaticFields));
        }

        return new DamlRecord(ToRuntimeIdentifier(record.RecordId), []);
    }

    public static DamlValue ToDamlValue(WireValue value)
    {
        if (value.Record is not null) return ToDamlRecord(value.Record);
        if (value.Variant is not null) return ToDamlVariant(value.Variant);
        if (value.List is not null) return ToDamlList(value.List);
        if (value.Optional is not null) return ToDamlOptional(value.Optional);
        if (value.TextMap is not null) return ToDamlTextMap(value.TextMap);
        if (value.GenMap is not null) return ToDamlGenMap(value.GenMap);
        if (value.Enum is not null) return ToDamlEnum(value.Enum);
        if (value.Int64 is not null) return new DamlInt64(ParseInt64(value.Int64));
        if (value.Numeric is not null) return new DamlNumeric(ParseNumeric(value.Numeric));
        if (value.Timestamp is not null) return DamlTimestamp.FromMicrosecondsSinceEpoch(ParseTimestamp(value.Timestamp));
        if (value.Party is not null) return new DamlParty(value.Party);
        if (value.Text is not null) return new DamlText(value.Text);
        if (value.ContractId is not null) return new DamlContractId(value.ContractId);
        if (value.Bool is { } boolean) return new DamlBool(boolean);
        if (value.Date is { } days) return DamlDate.FromDaysSinceEpoch(days);
        if (value.AdditionalProperties.ContainsKey(WireValueNames.Unit)) return DamlUnit.Instance;
        throw MalformedResponse("Received a wire Value with no recognisable sum case set.");
    }

    private static DamlVariant ToDamlVariant(WireVariant variant)
    {
        var inner = variant.Value
            ?? throw MalformedResponse($"Variant '{variant.Constructor}' has no value set.");
        return new DamlVariant(ToRuntimeIdentifier(variant.VariantId), variant.Constructor, ToDamlValue(inner));
    }

    private static DamlList ToDamlList(WireList list)
    {
        var elements = list.Elements ?? [];
        var values = new List<DamlValue>(elements.Count);
        foreach (var element in elements)
        {
            values.Add(ToDamlValue(element ?? throw MalformedResponse("List contains a null element.")));
        }
        return new DamlList(values);
    }

    private static DamlOptional ToDamlOptional(WireOptional optional) =>
        new(optional.Value is null ? null : ToDamlValue(optional.Value));

    private static DamlTextMap ToDamlTextMap(WireTextMap map)
    {
        var entries = map.Entries ?? [];
        var decoded = new Dictionary<string, DamlValue>(entries.Count);
        foreach (var entry in entries)
        {
            var entryValue = entry?.Value
                ?? throw MalformedResponse($"TextMap entry '{entry?.Key}' has no value set.");
            if (!decoded.TryAdd(entry.Key, ToDamlValue(entryValue)))
            {
                throw MalformedResponse($"TextMap contains duplicate key '{entry.Key}'.");
            }
        }
        return new DamlTextMap(decoded);
    }

    private static DamlGenMap ToDamlGenMap(WireGenMap map)
    {
        var entries = map.Entries ?? [];
        var pairs = new List<(DamlValue Key, DamlValue Value)>(entries.Count);
        foreach (var entry in entries)
        {
            var entryKey = entry?.Key ?? throw MalformedResponse("GenMap entry has no key set.");
            var entryValue = entry.Value ?? throw MalformedResponse("GenMap entry has no value set.");
            pairs.Add((ToDamlValue(entryKey), ToDamlValue(entryValue)));
        }
        return new DamlGenMap(pairs);
    }

    private static DamlEnum ToDamlEnum(WireEnum enumValue) =>
        new(ToRuntimeIdentifier(enumValue.EnumId), enumValue.Constructor);

    private static RuntimeIdentifier? ToRuntimeIdentifier(WireIdentifier? identifier) =>
        identifier is null
            ? null
            : new RuntimeIdentifier(identifier.PackageId, identifier.ModuleName, identifier.EntityName);

    private static long ParseInt64(string wire) =>
        long.TryParse(wire, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new FormatException($"Cannot parse wire Int64 value '{wire}' as a 64-bit integer.");

    private static decimal ParseNumeric(string wire) =>
        decimal.TryParse(wire, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new FormatException($"Cannot parse wire Numeric value '{wire}' as a decimal.");

    private static long ParseTimestamp(string wire) =>
        long.TryParse(wire, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new FormatException($"Cannot parse wire Timestamp value '{wire}' as microseconds since epoch.");
}
