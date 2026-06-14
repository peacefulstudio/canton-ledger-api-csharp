// Copyright 2026 Peaceful Studio OÜ

using System.Globalization;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;

namespace Daml.Runtime.Grpc;

/// <summary>
/// Bidirectional conversion between <see cref="DamlValue"/> (Runtime) and
/// <see cref="Value"/> (Canton Ledger API v2 protobuf).
/// </summary>
public static class DamlValueConverter
{
    /// <summary>Projects a Runtime <see cref="RuntimeIdentifier"/> onto its proto wire form.</summary>
    public static ProtoIdentifier ToProtoIdentifier(RuntimeIdentifier identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return new ProtoIdentifier
        {
            PackageId = identifier.PackageId,
            ModuleName = identifier.ModuleName,
            EntityName = identifier.EntityName
        };
    }

    /// <summary>
    /// Projects a Runtime template <see cref="RuntimeIdentifier"/> onto a proto identifier
    /// that references the template by package name (encoded as <c>#&lt;package-name&gt;</c>
    /// in the <c>package_id</c> field), as required by Canton read-path (stream-filter) endpoints.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="packageName"/> is null, empty, or whitespace. Canton's ACS and
    /// update filter endpoints reject the package-id hash form on filters, so a non-empty package
    /// name is required here; the message names the offending template and its package-id hash for
    /// diagnosis.
    /// </exception>
    public static ProtoIdentifier ToProtoTemplateNameIdentifier(string packageName, RuntimeIdentifier templateId)
    {
        ArgumentNullException.ThrowIfNull(templateId);

        if (string.IsNullOrWhiteSpace(packageName))
        {
            throw new ArgumentException(
                $"A non-empty package name is required to build a read/stream-filter identifier for " +
                $"template {templateId.ModuleName}:{templateId.EntityName} (package-id {templateId.PackageId}); " +
                $"Canton's ACS and update filter endpoints reject the package-id hash form on filters.",
                nameof(packageName));
        }

        return new ProtoIdentifier
        {
            PackageId = "#" + packageName,
            ModuleName = templateId.ModuleName,
            EntityName = templateId.EntityName
        };
    }

    /// <summary>Projects a Runtime <see cref="DamlRecord"/> onto its proto wire form.</summary>
    public static Record ToProtoRecord(DamlRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var protoRecord = new Record();

        if (record.RecordId is not null)
        {
            protoRecord.RecordId = ToProtoIdentifier(record.RecordId);
        }

        foreach (var field in record.Fields)
        {
            protoRecord.Fields.Add(new RecordField
            {
                Label = field.Label ?? string.Empty,
                Value = ToProtoValue(field.Value)
            });
        }

        return protoRecord;
    }

    /// <summary>
    /// Projects a Runtime <see cref="DamlValue"/> onto its proto wire form.
    /// <see cref="DamlNumeric"/> values are encoded in the canonical unpadded decimal form
    /// committed by codegen ADR-0011: trailing zeros stripped, at least one fractional digit,
    /// never scientific notation (e.g. <c>1.50m</c> → <c>"1.5"</c>, <c>0m</c> → <c>"0.0"</c>).
    /// Throws <see cref="NotSupportedException"/> for unrecognised <c>DamlValue</c>
    /// subclasses.
    /// </summary>
    public static Value ToProtoValue(DamlValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value switch
        {
            DamlUnit => new Value { Unit = new Google.Protobuf.WellKnownTypes.Empty() },
            DamlBool b => new Value { Bool = b.Value },
            DamlInt64 i => new Value { Int64 = i.Value },
            DamlText t => new Value { Text = t.Value },
            DamlParty p => new Value { Party = p.Value },
            DamlNumeric n => new Value { Numeric = n.Value.ToString(CanonicalNumericFormat, CultureInfo.InvariantCulture) },
            DamlDate d => new Value { Date = d.DaysSinceEpoch },
            DamlTimestamp ts => new Value { Timestamp = ts.MicrosecondsSinceEpoch },
            DamlContractId c => new Value { ContractId = c.Value },
            DamlRecord r => new Value { Record = ToProtoRecord(r) },
            DamlVariant v => new Value
            {
                Variant = new Variant
                {
                    Constructor = v.Constructor,
                    Value = ToProtoValue(v.Value),
                    VariantId = v.VariantId is not null ? ToProtoIdentifier(v.VariantId) : null
                }
            },
            DamlList l => ToProtoListValue(l),
            DamlOptional o => new Value
            {
                Optional = new Optional { Value = o.Value is not null ? ToProtoValue(o.Value) : null }
            },
            DamlTextMap m => ToProtoTextMapValue(m),
            DamlGenMap g => ToProtoGenMapValue(g),
            DamlEnum e => new Value
            {
                Enum = new Com.Daml.Ledger.Api.V2.Enum
                {
                    Constructor = e.Constructor,
                    EnumId = e.EnumId is not null ? ToProtoIdentifier(e.EnumId) : null
                }
            },
            _ => throw new NotSupportedException($"DamlValue type {value.GetType().Name} is not supported")
        };
    }

    /// <summary>Lifts a proto identifier into the Runtime <see cref="RuntimeIdentifier"/> type.</summary>
    public static RuntimeIdentifier FromProtoIdentifier(ProtoIdentifier identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return new RuntimeIdentifier(identifier.PackageId, identifier.ModuleName, identifier.EntityName);
    }

    /// <summary>
    /// Lifts a proto <see cref="Record"/> into the Runtime <see cref="DamlRecord"/>
    /// type, recursively converting each field's <see cref="Value"/> to its
    /// <see cref="DamlValue"/> equivalent.
    /// </summary>
    public static DamlRecord FromProtoRecord(Record record)
    {
        ArgumentNullException.ThrowIfNull(record);

        RuntimeIdentifier? recordId = record.RecordId is not null
            ? FromProtoIdentifier(record.RecordId)
            : null;

        var fields = new DamlField[record.Fields.Count];
        for (var i = 0; i < record.Fields.Count; i++)
        {
            var f = record.Fields[i];
            var fieldValue = f.Value
                ?? throw new InvalidOperationException(
                    $"Record field '{f.Label}' has no Value set.");
            fields[i] = new DamlField(f.Label, FromProtoValue(fieldValue));
        }

        return new DamlRecord(recordId, fields);
    }

    /// <summary>
    /// Lifts a proto <see cref="Value"/> into its Runtime <see cref="DamlValue"/>
    /// equivalent. Throws <see cref="InvalidOperationException"/> when the
    /// proto sum-case is unset (a malformed wire payload).
    /// </summary>
    public static DamlValue FromProtoValue(Value value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.SumCase switch
        {
            Value.SumOneofCase.None => throw new InvalidOperationException(
                "Received a proto Value with no value set (SumCase.None). " +
                "This typically indicates a malformed response from the ledger."),
            Value.SumOneofCase.Unit => DamlUnit.Instance,
            Value.SumOneofCase.Bool => new DamlBool(value.Bool),
            Value.SumOneofCase.Int64 => new DamlInt64(value.Int64),
            Value.SumOneofCase.Text => new DamlText(value.Text),
            Value.SumOneofCase.Party => new DamlParty(value.Party),
            Value.SumOneofCase.Numeric => decimal.TryParse(value.Numeric, CultureInfo.InvariantCulture, out var parsed)
                ? new DamlNumeric(parsed)
                : throw new FormatException(
                    $"Cannot parse proto Numeric value '{value.Numeric}' as decimal."),
            Value.SumOneofCase.Date => DamlDate.FromDaysSinceEpoch(value.Date),
            Value.SumOneofCase.Timestamp => DamlTimestamp.FromMicrosecondsSinceEpoch(value.Timestamp),
            Value.SumOneofCase.ContractId => new DamlContractId(value.ContractId),
            Value.SumOneofCase.Record => FromProtoRecord(
                RequireMessage(value.Record, Value.SumOneofCase.Record)),
            Value.SumOneofCase.Variant => FromProtoVariantValue(
                RequireMessage(value.Variant, Value.SumOneofCase.Variant)),
            Value.SumOneofCase.List => new DamlList(
                RequireMessage(value.List, Value.SumOneofCase.List)
                    .Elements.Select(FromProtoValue).ToList()),
            Value.SumOneofCase.Optional => FromProtoOptionalValue(
                RequireMessage(value.Optional, Value.SumOneofCase.Optional)),
            Value.SumOneofCase.TextMap => FromProtoTextMap(
                RequireMessage(value.TextMap, Value.SumOneofCase.TextMap)),
            Value.SumOneofCase.GenMap => FromProtoGenMap(
                RequireMessage(value.GenMap, Value.SumOneofCase.GenMap)),
            Value.SumOneofCase.Enum => FromProtoEnumValue(
                RequireMessage(value.Enum, Value.SumOneofCase.Enum)),
            _ => throw new NotSupportedException($"Proto Value case {value.SumCase} is not supported")
        };
    }

    /// <summary>
    /// Canonical unpadded numeric wire format (codegen ADR-0011): one forced fractional digit
    /// followed by 27 optional fractional digits — 28 in total, matching the maximum scale of
    /// <see cref="decimal"/>, so no representable value is ever rounded on the wire.
    /// </summary>
    private const string CanonicalNumericFormat = "0.0###########################";

    private static T RequireMessage<T>(T? message, Value.SumOneofCase sumCase) where T : class =>
        message ?? throw new InvalidOperationException(
            $"Received a malformed proto Value: SumCase.{sumCase} is set but {sumCase} message is null.");

    private static DamlVariant FromProtoVariantValue(Variant variant)
    {
        var innerValue = variant.Value
            ?? throw new InvalidOperationException(
                $"Variant '{variant.Constructor}' has no Value set.");
        return new DamlVariant(
            variant.VariantId is not null ? FromProtoIdentifier(variant.VariantId) : null,
            variant.Constructor,
            FromProtoValue(innerValue));
    }

    private static DamlOptional FromProtoOptionalValue(Optional optional) =>
        new(optional.Value is not null ? FromProtoValue(optional.Value) : null);

    private static DamlEnum FromProtoEnumValue(Com.Daml.Ledger.Api.V2.Enum enumValue) =>
        new(
            enumValue.EnumId is not null ? FromProtoIdentifier(enumValue.EnumId) : null,
            enumValue.Constructor);

    private static DamlTextMap FromProtoTextMap(TextMap map)
    {
        var dict = new Dictionary<string, DamlValue>(map.Entries.Count);
        foreach (var entry in map.Entries)
        {
            var entryValue = entry.Value
                ?? throw new InvalidOperationException(
                    $"TextMap entry '{entry.Key}' has no Value set.");
            if (!dict.TryAdd(entry.Key, FromProtoValue(entryValue)))
                throw new InvalidOperationException(
                    $"TextMap contains duplicate key '{entry.Key}'.");
        }
        return new DamlTextMap(dict);
    }

    private static DamlGenMap FromProtoGenMap(GenMap map)
    {
        var entries = new List<(DamlValue Key, DamlValue Value)>(map.Entries.Count);
        foreach (var entry in map.Entries)
        {
            var entryKey = entry.Key
                ?? throw new InvalidOperationException("GenMap entry has no Key set.");
            var entryValue = entry.Value
                ?? throw new InvalidOperationException("GenMap entry has no Value set.");
            entries.Add((FromProtoValue(entryKey), FromProtoValue(entryValue)));
        }
        return new DamlGenMap(entries);
    }

    private static Value ToProtoListValue(DamlList list)
    {
        var protoList = new List();
        foreach (var item in list.Values)
        {
            protoList.Elements.Add(ToProtoValue(item));
        }
        return new Value { List = protoList };
    }

    private static Value ToProtoTextMapValue(DamlTextMap map)
    {
        var protoMap = new TextMap();
        foreach (var kvp in map.Values)
        {
            protoMap.Entries.Add(new TextMap.Types.Entry
            {
                Key = kvp.Key,
                Value = ToProtoValue(kvp.Value)
            });
        }
        return new Value { TextMap = protoMap };
    }

    private static Value ToProtoGenMapValue(DamlGenMap map)
    {
        var protoMap = new GenMap();
        foreach (var entry in map.Entries)
        {
            protoMap.Entries.Add(new GenMap.Types.Entry
            {
                Key = ToProtoValue(entry.Key),
                Value = ToProtoValue(entry.Value)
            });
        }
        return new Value { GenMap = protoMap };
    }
}
