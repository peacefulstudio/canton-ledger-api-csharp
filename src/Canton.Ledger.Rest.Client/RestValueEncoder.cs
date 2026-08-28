// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using WireEnum = Canton.Ledger.Rest.Client.Raw.Enum;
using WireGenMap = Canton.Ledger.Rest.Client.Raw.GenMap;
using WireGenMapEntry = Canton.Ledger.Rest.Client.Raw.GenMap_Entry;
using WireIdentifier = Canton.Ledger.Rest.Client.Raw.Identifier;
using WireList = Canton.Ledger.Rest.Client.Raw.List;
using WireOptional = Canton.Ledger.Rest.Client.Raw.Optional;
using WireRecord = Canton.Ledger.Rest.Client.Raw.Record;
using WireRecordField = Canton.Ledger.Rest.Client.Raw.RecordField;
using WireTextMap = Canton.Ledger.Rest.Client.Raw.TextMap;
using WireTextMapEntry = Canton.Ledger.Rest.Client.Raw.TextMap_Entry;
using WireValue = Canton.Ledger.Rest.Client.Raw.Value;
using WireVariant = Canton.Ledger.Rest.Client.Raw.Variant;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Encodes runtime <see cref="DamlValue"/>/<see cref="DamlRecord"/> instances into the generated
/// wire shape (<see cref="Raw.Value"/>/<see cref="Raw.Record"/>) for command submission. This is
/// the mirror image of <see cref="RestValueDecoder"/>.
/// </summary>
internal static class RestValueEncoder
{
    public static WireRecord ToWireRecord(DamlRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var fields = new List<WireRecordField>(record.Fields.Count);
        foreach (var field in record.Fields)
        {
            fields.Add(new WireRecordField { Label = field.Label, Value = ToWireValue(field.Value) });
        }

        return new WireRecord
        {
            RecordId = ToWireIdentifier(record.RecordId)!,
            Fields = fields,
        };
    }

    public static WireValue ToWireValue(DamlValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value switch
        {
            DamlBool damlBool => new WireValue { Bool = damlBool.Value },
            DamlInt64 damlInt64 => new WireValue { Int64 = damlInt64.Value.ToString(CultureInfo.InvariantCulture) },
            DamlNumeric damlNumeric => new WireValue { Numeric = damlNumeric.ToCanonicalString() },
            DamlDate damlDate => new WireValue { Date = damlDate.DaysSinceEpoch },
            DamlTimestamp damlTimestamp => new WireValue
            {
                Timestamp = damlTimestamp.MicrosecondsSinceEpoch.ToString(CultureInfo.InvariantCulture),
            },
            DamlParty damlParty => new WireValue { Party = damlParty.Value },
            DamlText damlText => new WireValue { Text = damlText.Value },
            DamlContractId damlContractId => new WireValue { ContractId = damlContractId.Value },
            DamlUnit => UnitValue(),
            DamlOptional damlOptional => new WireValue
            {
                Optional = new WireOptional
                {
                    Value = damlOptional.Value is null ? null! : ToWireValue(damlOptional.Value),
                },
            },
            DamlList damlList => new WireValue
            {
                List = new WireList { Elements = damlList.Values.Select(ToWireValue).ToList() },
            },
            DamlTextMap damlTextMap => new WireValue
            {
                TextMap = new WireTextMap
                {
                    Entries = damlTextMap.Values
                        .Select(kv => new WireTextMapEntry { Key = kv.Key, Value = ToWireValue(kv.Value) })
                        .ToList(),
                },
            },
            DamlGenMap damlGenMap => new WireValue
            {
                GenMap = new WireGenMap
                {
                    Entries = damlGenMap.Entries
                        .Select(entry => new WireGenMapEntry
                        {
                            Key = ToWireValue(entry.Item1),
                            Value = ToWireValue(entry.Item2),
                        })
                        .ToList(),
                },
            },
            DamlVariant damlVariant => new WireValue
            {
                Variant = new WireVariant
                {
                    VariantId = ToWireIdentifier(damlVariant.VariantId)!,
                    Constructor = damlVariant.Constructor,
                    Value = ToWireValue(damlVariant.Value),
                },
            },
            DamlEnum damlEnum => new WireValue
            {
                Enum = new WireEnum { EnumId = ToWireIdentifier(damlEnum.EnumId)!, Constructor = damlEnum.Constructor },
            },
            DamlRecord damlRecord => new WireValue { Record = ToWireRecord(damlRecord) },
            _ => throw new NotSupportedException($"DamlValue type {value.GetType().Name} is not supported for wire encoding."),
        };
    }

    private static WireValue UnitValue()
    {
        var wireValue = new WireValue();
        wireValue.AdditionalProperties[WireValueNames.Unit] = new object();
        return wireValue;
    }

    private static WireIdentifier? ToWireIdentifier(RuntimeIdentifier? identifier) =>
        identifier is null
            ? null
            : new WireIdentifier
            {
                PackageId = identifier.PackageId,
                ModuleName = identifier.ModuleName,
                EntityName = identifier.EntityName,
            };
}
