// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

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
    // ──────────────────────────────────────────────────────────────
    // ToProto*
    // ──────────────────────────────────────────────────────────────

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
            DamlNumeric n => new Value { Numeric = n.Value.ToString(CultureInfo.InvariantCulture) },
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

    // ──────────────────────────────────────────────────────────────
    // FromProto*
    // ──────────────────────────────────────────────────────────────

    public static RuntimeIdentifier FromProtoIdentifier(ProtoIdentifier identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return new RuntimeIdentifier(identifier.PackageId, identifier.ModuleName, identifier.EntityName);
    }

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

    // ──────────────────────────────────────────────────────────────
    // FromDamlValue<T> — unwrap DamlValue to CLR type
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a <see cref="DamlValue"/> to a CLR type.
    /// Handles primitive unwrapping:
    /// <c>string</c> (from <see cref="DamlText"/>, <see cref="DamlParty"/>, or <see cref="DamlContractId"/>),
    /// <c>long</c>, <c>bool</c>, <c>decimal</c>, <c>DateOnly</c>, <c>DateTimeOffset</c>, <see cref="Party"/>;
    /// <see cref="DamlUnit"/> → <c>default(T)</c> for reference and nullable value types; and
    /// <see cref="DamlContractId"/> → <see cref="ContractId{T}"/>.
    /// Falls back to a direct cast for assignable <see cref="DamlValue"/> subtypes.
    /// </summary>
    public static TResult FromDamlValue<TResult>(DamlValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        // Assignable check runs first so that FromDamlValue<DamlUnit>(DamlUnit.Instance)
        // and FromDamlValue<DamlValue>(DamlUnit.Instance) return the instance rather than default.
        if (typeof(TResult).IsAssignableFrom(value.GetType()))
            return (TResult)(object)value;

        if (value is DamlUnit)
        {
            // Nullable<T> is a struct but can represent null, so default(T?) is a valid "no value".
            if (typeof(TResult).IsValueType && Nullable.GetUnderlyingType(typeof(TResult)) is null)
                throw new NotSupportedException(
                    $"Cannot convert DamlUnit to value type {typeof(TResult).Name}. " +
                    $"Unit represents 'no value' and has no meaningful conversion to {typeof(TResult).Name}.");
            return default!;
        }

        if (typeof(TResult) == typeof(string))
        {
            return value switch
            {
                DamlText text => (TResult)(object)text.Value,
                DamlParty party => (TResult)(object)party.Value,
                DamlContractId contractId => (TResult)(object)contractId.Value,
                _ => throw new NotSupportedException(
                    $"Cannot convert {value.GetType().Name} to string. " +
                    $"Only DamlText, DamlParty, and DamlContractId can be unwrapped to string.")
            };
        }

        if (typeof(TResult) == typeof(long) && value is DamlInt64 i64)
            return (TResult)(object)i64.Value;

        if (typeof(TResult) == typeof(bool) && value is DamlBool b)
            return (TResult)(object)b.Value;

        if (typeof(TResult) == typeof(decimal) && value is DamlNumeric n)
            return (TResult)(object)n.Value;

        if (typeof(TResult) == typeof(DateOnly) && value is DamlDate d)
            return (TResult)(object)d.Value;

        if (typeof(TResult) == typeof(DateTimeOffset) && value is DamlTimestamp ts)
            return (TResult)(object)ts.Value;

        if (typeof(TResult) == typeof(Party) && value is DamlParty p)
            return (TResult)(object)Party.FromDamlValue(p);

        if (value is DamlContractId cid && typeof(TResult).IsGenericType
            && typeof(TResult).GetGenericTypeDefinition() == typeof(ContractId<>))
        {
            var instance = Activator.CreateInstance(typeof(TResult), cid.Value)
                ?? throw new InvalidOperationException(
                    $"Failed to create {typeof(TResult).Name} from contract ID '{cid.Value}'. " +
                    $"Ensure {typeof(TResult).Name} has a public constructor accepting a string.");
            return (TResult)instance;
        }

        throw new NotSupportedException(
            $"Cannot convert {value.GetType().Name} to {typeof(TResult).Name}. " +
            $"Use a DamlValue-derived type as TResult for direct access.");
    }

    // ──────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────

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
