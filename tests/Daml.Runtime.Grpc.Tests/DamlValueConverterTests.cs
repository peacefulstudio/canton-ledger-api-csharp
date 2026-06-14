// Copyright © 2026 Peaceful Studio OÜ. All rights reserved.

using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using FluentAssertions;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;

namespace Daml.Runtime.Grpc.Tests;

public class DamlValueConverterTests
{
    [Fact]
    public void ToProtoIdentifier_converts_correctly()
    {
        var identifier = new RuntimeIdentifier("package-id", "Module.Name", "Entity");

        var protoIdentifier = DamlValueConverter.ToProtoIdentifier(identifier);

        protoIdentifier.PackageId.Should().Be("package-id");
        protoIdentifier.ModuleName.Should().Be("Module.Name");
        protoIdentifier.EntityName.Should().Be("Entity");
    }

    [Fact]
    public void ToProtoTemplateNameIdentifier_emits_hash_prefixed_package_name()
    {
        var identifier = new RuntimeIdentifier("9b63deadbeefhash", "RichTypes", "RichRecord");

        var result = DamlValueConverter.ToProtoTemplateNameIdentifier("richtypes", identifier);

        result.PackageId.Should().Be("#richtypes");
        result.ModuleName.Should().Be("RichTypes");
        result.EntityName.Should().Be("RichRecord");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToProtoTemplateNameIdentifier_throws_when_package_name_missing(string? packageName)
    {
        var identifier = new RuntimeIdentifier("9b63deadbeefhash", "RichTypes", "RichRecord");

        var action = () => DamlValueConverter.ToProtoTemplateNameIdentifier(packageName!, identifier);

        action.Should().Throw<ArgumentException>()
            .WithMessage("*RichTypes*RichRecord*9b63deadbeefhash*")
            .And.ParamName.Should().Be("packageName");
    }

    [Fact]
    public void ToProtoTemplateNameIdentifier_throws_when_template_id_null()
    {
        var action = () => DamlValueConverter.ToProtoTemplateNameIdentifier("richtypes", null!);

        action.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("templateId");
    }

    [Fact]
    public void ToProtoValue_converts_unit()
    {
        var protoValue = DamlValueConverter.ToProtoValue(DamlUnit.Instance);

        protoValue.Unit.Should().NotBeNull();
    }

    [Fact]
    public void ToProtoValue_converts_bool()
    {
        var protoTrue = DamlValueConverter.ToProtoValue(new DamlBool(true));
        var protoFalse = DamlValueConverter.ToProtoValue(new DamlBool(false));

        protoTrue.Bool.Should().BeTrue();
        protoFalse.Bool.Should().BeFalse();
    }

    [Fact]
    public void ToProtoValue_converts_int64()
    {
        var protoValue = DamlValueConverter.ToProtoValue(new DamlInt64(42));

        protoValue.Int64.Should().Be(42);
    }

    [Fact]
    public void ToProtoValue_converts_int64_boundaries()
    {
        DamlValueConverter.ToProtoValue(new DamlInt64(0)).Int64.Should().Be(0);
        DamlValueConverter.ToProtoValue(new DamlInt64(long.MinValue)).Int64.Should().Be(long.MinValue);
        DamlValueConverter.ToProtoValue(new DamlInt64(long.MaxValue)).Int64.Should().Be(long.MaxValue);
    }

    [Fact]
    public void ToProtoValue_converts_text()
    {
        var protoValue = DamlValueConverter.ToProtoValue(new DamlText("hello world"));

        protoValue.Text.Should().Be("hello world");
    }

    [Fact]
    public void ToProtoValue_converts_party()
    {
        var protoValue = DamlValueConverter.ToProtoValue(new DamlParty("party::alice"));

        protoValue.Party.Should().Be("party::alice");
    }

    [Fact]
    public void ToProtoValue_converts_numeric()
    {
        var protoValue = DamlValueConverter.ToProtoValue(new DamlNumeric(123.456m));

        protoValue.Numeric.Should().Be("123.456");
    }

    [Fact]
    public void ToProtoValue_converts_numeric_zero_to_canonical_form()
    {
        DamlValueConverter.ToProtoValue(new DamlNumeric(0m)).Numeric.Should().Be("0.0");
    }

    [Fact]
    public void ToProtoValue_converts_integer_numeric_with_single_trailing_zero()
    {
        DamlValueConverter.ToProtoValue(new DamlNumeric(42m)).Numeric.Should().Be("42.0");
    }

    [Fact]
    public void ToProtoValue_strips_trailing_zeros_from_numeric()
    {
        DamlValueConverter.ToProtoValue(new DamlNumeric(1.50m)).Numeric.Should().Be("1.5");
    }

    [Fact]
    public void ToProtoValue_numeric_form_is_independent_of_construction_scale()
    {
        var fromShort = DamlValueConverter.ToProtoValue(new DamlNumeric(1.5m)).Numeric;
        var fromPadded = DamlValueConverter.ToProtoValue(new DamlNumeric(1.500000m)).Numeric;

        fromShort.Should().Be("1.5");
        fromPadded.Should().Be(fromShort);
    }

    [Fact]
    public void ToProtoValue_keeps_sign_and_strips_trailing_zeros_from_negative_numeric()
    {
        DamlValueConverter.ToProtoValue(new DamlNumeric(-1.50m)).Numeric.Should().Be("-1.5");
        DamlValueConverter.ToProtoValue(new DamlNumeric(-42m)).Numeric.Should().Be("-42.0");
    }

    [Fact]
    public void ToProtoValue_converts_negative_zero_numeric_to_canonical_zero()
    {
        DamlValueConverter.ToProtoValue(new DamlNumeric(-0.0m)).Numeric.Should().Be("0.0");
    }

    [Fact]
    public void ToProtoValue_preserves_scale_28_smallest_decimal_without_scientific_notation()
    {
        DamlValueConverter.ToProtoValue(new DamlNumeric(0.0000000000000000000000000001m))
            .Numeric.Should().Be("0.0000000000000000000000000001");
    }

    [Fact]
    public void ToProtoValue_preserves_all_28_fractional_digits_of_numeric()
    {
        DamlValueConverter.ToProtoValue(new DamlNumeric(0.1234567890123456789012345678m))
            .Numeric.Should().Be("0.1234567890123456789012345678");
    }

    [Fact]
    public void ToProtoValue_converts_decimal_MaxValue_with_forced_fractional_digit()
    {
        DamlValueConverter.ToProtoValue(new DamlNumeric(decimal.MaxValue))
            .Numeric.Should().Be("79228162514264337593543950335.0");
    }

    [Fact]
    public void ToProtoValue_converts_date()
    {
        var date = new DateOnly(2024, 1, 1);
        var value = new DamlDate(date);

        var protoValue = DamlValueConverter.ToProtoValue(value);

        protoValue.Date.Should().Be(value.DaysSinceEpoch);
    }

    [Fact]
    public void ToProtoValue_converts_timestamp()
    {
        var timestamp = DateTimeOffset.UnixEpoch.AddSeconds(1704067200);
        var value = new DamlTimestamp(timestamp);

        var protoValue = DamlValueConverter.ToProtoValue(value);

        protoValue.Timestamp.Should().Be(value.MicrosecondsSinceEpoch);
    }

    [Fact]
    public void ToProtoValue_converts_record()
    {
        var record = new DamlRecord(
            new RuntimeIdentifier("pkg", "Module", "Record"),
            [
                new DamlField("name", new DamlText("Alice")),
                new DamlField("age", new DamlInt64(30))
            ]);

        var protoValue = DamlValueConverter.ToProtoValue(record);

        protoValue.Record.Should().NotBeNull();
        protoValue.Record.RecordId.PackageId.Should().Be("pkg");
        protoValue.Record.Fields.Should().HaveCount(2);
        protoValue.Record.Fields[0].Label.Should().Be("name");
        protoValue.Record.Fields[0].Value.Text.Should().Be("Alice");
        protoValue.Record.Fields[1].Label.Should().Be("age");
        protoValue.Record.Fields[1].Value.Int64.Should().Be(30);
    }

    [Fact]
    public void ToProtoValue_converts_variant()
    {
        var variant = new DamlVariant(
            new RuntimeIdentifier("pkg", "Module", "Variant"),
            "Some",
            new DamlText("value"));

        var protoValue = DamlValueConverter.ToProtoValue(variant);

        protoValue.Variant.Should().NotBeNull();
        protoValue.Variant.Constructor.Should().Be("Some");
        protoValue.Variant.Value.Text.Should().Be("value");
        protoValue.Variant.VariantId.PackageId.Should().Be("pkg");
    }

    [Fact]
    public void ToProtoValue_converts_variant_without_id()
    {
        var variant = new DamlVariant(null, "Left", new DamlText("value"));

        var protoValue = DamlValueConverter.ToProtoValue(variant);

        protoValue.Variant.Constructor.Should().Be("Left");
        protoValue.Variant.VariantId.Should().BeNull();
    }

    [Fact]
    public void ToProtoValue_converts_list()
    {
        var list = new DamlList([
            new DamlInt64(1),
            new DamlInt64(2),
            new DamlInt64(3)
        ]);

        var protoValue = DamlValueConverter.ToProtoValue(list);

        protoValue.List.Should().NotBeNull();
        protoValue.List.Elements.Should().HaveCount(3);
        protoValue.List.Elements[0].Int64.Should().Be(1);
        protoValue.List.Elements[1].Int64.Should().Be(2);
        protoValue.List.Elements[2].Int64.Should().Be(3);
    }

    [Fact]
    public void ToProtoValue_converts_empty_list()
    {
        var protoValue = DamlValueConverter.ToProtoValue(new DamlList([]));

        protoValue.List.Elements.Should().BeEmpty();
    }

    [Fact]
    public void ToProtoValue_converts_optional_with_value()
    {
        var optional = new DamlOptional(new DamlText("present"));

        var protoValue = DamlValueConverter.ToProtoValue(optional);

        protoValue.Optional.Should().NotBeNull();
        protoValue.Optional.Value.Text.Should().Be("present");
    }

    [Fact]
    public void ToProtoValue_converts_optional_without_value()
    {
        var optional = new DamlOptional(null);

        var protoValue = DamlValueConverter.ToProtoValue(optional);

        protoValue.Optional.Should().NotBeNull();
        protoValue.Optional.Value.Should().BeNull();
    }

    [Fact]
    public void ToProtoValue_converts_text_map()
    {
        var map = new DamlTextMap(new Dictionary<string, DamlValue>
        {
            ["key1"] = new DamlText("value1"),
            ["key2"] = new DamlText("value2")
        });

        var protoValue = DamlValueConverter.ToProtoValue(map);

        protoValue.TextMap.Should().NotBeNull();
        protoValue.TextMap.Entries.Should().HaveCount(2);
    }

    [Fact]
    public void ToProtoValue_converts_empty_text_map()
    {
        var protoValue = DamlValueConverter.ToProtoValue(
            new DamlTextMap(new Dictionary<string, DamlValue>()));

        protoValue.TextMap.Entries.Should().BeEmpty();
    }

    [Fact]
    public void ToProtoValue_converts_gen_map()
    {
        var map = new DamlGenMap([
            (new DamlInt64(1), new DamlText("one")),
            (new DamlInt64(2), new DamlText("two"))
        ]);

        var protoValue = DamlValueConverter.ToProtoValue(map);

        protoValue.GenMap.Should().NotBeNull();
        protoValue.GenMap.Entries.Should().HaveCount(2);
        protoValue.GenMap.Entries[0].Key.Int64.Should().Be(1);
        protoValue.GenMap.Entries[0].Value.Text.Should().Be("one");
    }

    [Fact]
    public void ToProtoValue_converts_empty_gen_map()
    {
        var protoValue = DamlValueConverter.ToProtoValue(new DamlGenMap([]));

        protoValue.GenMap.Entries.Should().BeEmpty();
    }

    [Fact]
    public void ToProtoValue_converts_enum()
    {
        var enumValue = new DamlEnum(
            new RuntimeIdentifier("pkg", "Module", "Color"),
            "Red");

        var protoValue = DamlValueConverter.ToProtoValue(enumValue);

        protoValue.Enum.Should().NotBeNull();
        protoValue.Enum.Constructor.Should().Be("Red");
        protoValue.Enum.EnumId.PackageId.Should().Be("pkg");
    }

    [Fact]
    public void ToProtoValue_converts_enum_without_id()
    {
        var enumValue = new DamlEnum(null, "Green");

        var protoValue = DamlValueConverter.ToProtoValue(enumValue);

        protoValue.Enum.Constructor.Should().Be("Green");
        protoValue.Enum.EnumId.Should().BeNull();
    }

    [Fact]
    public void ToProtoValue_throws_for_unsupported_daml_value()
    {
        var action = () => DamlValueConverter.ToProtoValue(new UnsupportedDamlValue());

        action.Should().Throw<NotSupportedException>()
            .WithMessage("*UnsupportedDamlValue*");
    }

    [Fact]
    public void ToProtoRecord_converts_correctly()
    {
        var record = new DamlRecord(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            [
                new DamlField("owner", new DamlParty("party::alice")),
                new DamlField("value", new DamlInt64(100))
            ]);

        var protoRecord = DamlValueConverter.ToProtoRecord(record);

        protoRecord.RecordId.Should().NotBeNull();
        protoRecord.RecordId.PackageId.Should().Be("pkg");
        protoRecord.RecordId.ModuleName.Should().Be("Module");
        protoRecord.RecordId.EntityName.Should().Be("Template");
        protoRecord.Fields.Should().HaveCount(2);
    }

    [Fact]
    public void ToProtoRecord_handles_null_record_id()
    {
        var record = new DamlRecord(null, [
            new DamlField("field", new DamlText("value"))
        ]);

        var protoRecord = DamlValueConverter.ToProtoRecord(record);

        protoRecord.RecordId.Should().BeNull();
        protoRecord.Fields.Should().ContainSingle();
    }

    [Fact]
    public void ToProtoRecord_handles_empty_fields()
    {
        var record = new DamlRecord(
            new RuntimeIdentifier("pkg", "Module", "Empty"), []);

        var protoRecord = DamlValueConverter.ToProtoRecord(record);

        protoRecord.RecordId.Should().NotBeNull();
        protoRecord.Fields.Should().BeEmpty();
    }

    [Fact]
    public void FromProtoIdentifier_converts_correctly()
    {
        var proto = new ProtoIdentifier
        {
            PackageId = "package-id",
            ModuleName = "Module.Name",
            EntityName = "Entity"
        };

        var result = DamlValueConverter.FromProtoIdentifier(proto);

        result.PackageId.Should().Be("package-id");
        result.ModuleName.Should().Be("Module.Name");
        result.EntityName.Should().Be("Entity");
    }

    [Fact]
    public void FromProtoValue_converts_unit()
    {
        var proto = new ProtoValue { Unit = new Google.Protobuf.WellKnownTypes.Empty() };

        var result = DamlValueConverter.FromProtoValue(proto);

        result.Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void FromProtoValue_converts_bool()
    {
        DamlValueConverter.FromProtoValue(new ProtoValue { Bool = true }).Should().Be(new DamlBool(true));
        DamlValueConverter.FromProtoValue(new ProtoValue { Bool = false }).Should().Be(new DamlBool(false));
    }

    [Fact]
    public void FromProtoValue_converts_int64()
    {
        var result = DamlValueConverter.FromProtoValue(new ProtoValue { Int64 = 42 });

        result.Should().Be(new DamlInt64(42));
    }

    [Fact]
    public void FromProtoValue_converts_int64_boundaries()
    {
        DamlValueConverter.FromProtoValue(new ProtoValue { Int64 = 0 }).Should().Be(new DamlInt64(0));
        DamlValueConverter.FromProtoValue(new ProtoValue { Int64 = long.MinValue }).Should().Be(new DamlInt64(long.MinValue));
        DamlValueConverter.FromProtoValue(new ProtoValue { Int64 = long.MaxValue }).Should().Be(new DamlInt64(long.MaxValue));
    }

    [Fact]
    public void FromProtoValue_converts_text()
    {
        var result = DamlValueConverter.FromProtoValue(new ProtoValue { Text = "hello world" });

        result.Should().Be(new DamlText("hello world"));
    }

    [Fact]
    public void FromProtoValue_converts_empty_text()
    {
        DamlValueConverter.FromProtoValue(new ProtoValue { Text = "" }).Should().Be(new DamlText(""));
    }

    [Fact]
    public void FromProtoValue_converts_party()
    {
        var result = DamlValueConverter.FromProtoValue(new ProtoValue { Party = "party::alice" });

        result.Should().Be(new DamlParty("party::alice"));
    }

    [Fact]
    public void FromProtoValue_converts_numeric()
    {
        var result = DamlValueConverter.FromProtoValue(new ProtoValue { Numeric = "123.456" });

        result.Should().BeOfType<DamlNumeric>();
        result.As<DamlNumeric>().Value.Should().Be(123.456m);
    }

    [Fact]
    public void FromProtoValue_converts_numeric_zero()
    {
        DamlValueConverter.FromProtoValue(new ProtoValue { Numeric = "0" })
            .As<DamlNumeric>().Value.Should().Be(0m);
    }

    [Fact]
    public void FromProtoValue_converts_negative_numeric()
    {
        DamlValueConverter.FromProtoValue(new ProtoValue { Numeric = "-1.23" })
            .As<DamlNumeric>().Value.Should().Be(-1.23m);
    }

    [Fact]
    public void FromProtoValue_throws_for_malformed_numeric()
    {
        var action = () => DamlValueConverter.FromProtoValue(new ProtoValue { Numeric = "NaN" });

        action.Should().Throw<FormatException>()
            .WithMessage("*'NaN'*");
    }

    [Fact]
    public void FromProtoValue_converts_date()
    {
        var original = new DamlDate(new DateOnly(2024, 1, 1));
        var proto = new ProtoValue { Date = original.DaysSinceEpoch };

        var result = DamlValueConverter.FromProtoValue(proto);

        result.Should().BeOfType<DamlDate>();
        result.As<DamlDate>().Value.Should().Be(new DateOnly(2024, 1, 1));
    }

    [Fact]
    public void FromProtoValue_converts_date_at_epoch()
    {
        DamlValueConverter.FromProtoValue(new ProtoValue { Date = 0 })
            .As<DamlDate>().Value.Should().Be(new DateOnly(1970, 1, 1));
    }

    [Fact]
    public void FromProtoValue_converts_timestamp()
    {
        var original = new DamlTimestamp(DateTimeOffset.UnixEpoch.AddSeconds(1704067200));
        var proto = new ProtoValue { Timestamp = original.MicrosecondsSinceEpoch };

        var result = DamlValueConverter.FromProtoValue(proto);

        result.Should().BeOfType<DamlTimestamp>();
        result.As<DamlTimestamp>().MicrosecondsSinceEpoch.Should().Be(original.MicrosecondsSinceEpoch);
    }

    [Fact]
    public void FromProtoValue_converts_timestamp_at_epoch()
    {
        DamlValueConverter.FromProtoValue(new ProtoValue { Timestamp = 0 })
            .As<DamlTimestamp>().Value.Should().Be(DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void FromProtoValue_converts_contract_id()
    {
        var result = DamlValueConverter.FromProtoValue(new ProtoValue { ContractId = "00abc123" });

        result.Should().BeOfType<DamlContractId>();
        result.As<DamlContractId>().Value.Should().Be("00abc123");
    }

    [Fact]
    public void FromProtoValue_converts_record()
    {
        var protoRecord = new ProtoRecord();
        protoRecord.RecordId = new ProtoIdentifier
        {
            PackageId = "pkg", ModuleName = "Module", EntityName = "Record"
        };
        protoRecord.Fields.Add(new RecordField { Label = "name", Value = new ProtoValue { Text = "Alice" } });
        protoRecord.Fields.Add(new RecordField { Label = "age", Value = new ProtoValue { Int64 = 30 } });

        var result = DamlValueConverter.FromProtoValue(new ProtoValue { Record = protoRecord });

        result.Should().BeOfType<DamlRecord>();
        var record = result.As<DamlRecord>();
        record.RecordId!.PackageId.Should().Be("pkg");
        record.Fields.Should().HaveCount(2);
        record.Fields[0].Label.Should().Be("name");
        record.Fields[0].Value.Should().Be(new DamlText("Alice"));
        record.Fields[1].Label.Should().Be("age");
        record.Fields[1].Value.Should().Be(new DamlInt64(30));
    }

    [Fact]
    public void FromProtoValue_converts_variant()
    {
        var proto = new ProtoValue
        {
            Variant = new Variant
            {
                VariantId = new ProtoIdentifier { PackageId = "pkg", ModuleName = "Module", EntityName = "Variant" },
                Constructor = "Some",
                Value = new ProtoValue { Text = "value" }
            }
        };

        var result = DamlValueConverter.FromProtoValue(proto);

        result.Should().BeOfType<DamlVariant>();
        var variant = result.As<DamlVariant>();
        variant.Constructor.Should().Be("Some");
        variant.Value.Should().Be(new DamlText("value"));
        variant.VariantId!.PackageId.Should().Be("pkg");
    }

    [Fact]
    public void FromProtoValue_converts_variant_without_id()
    {
        var proto = new ProtoValue
        {
            Variant = new Variant { Constructor = "Left", Value = new ProtoValue { Int64 = 42 } }
        };

        var result = DamlValueConverter.FromProtoValue(proto);

        result.As<DamlVariant>().VariantId.Should().BeNull();
        result.As<DamlVariant>().Constructor.Should().Be("Left");
    }

    [Fact]
    public void FromProtoValue_converts_list()
    {
        var protoList = new List();
        protoList.Elements.Add(new ProtoValue { Int64 = 1 });
        protoList.Elements.Add(new ProtoValue { Int64 = 2 });
        protoList.Elements.Add(new ProtoValue { Int64 = 3 });

        var result = DamlValueConverter.FromProtoValue(new ProtoValue { List = protoList });

        result.Should().BeOfType<DamlList>();
        var list = result.As<DamlList>();
        list.Values.Should().HaveCount(3);
        list.Values[0].Should().Be(new DamlInt64(1));
        list.Values[1].Should().Be(new DamlInt64(2));
        list.Values[2].Should().Be(new DamlInt64(3));
    }

    [Fact]
    public void FromProtoValue_converts_empty_list()
    {
        var result = DamlValueConverter.FromProtoValue(new ProtoValue { List = new List() });

        result.As<DamlList>().Values.Should().BeEmpty();
    }

    [Fact]
    public void FromProtoValue_converts_optional_with_value()
    {
        var proto = new ProtoValue
        {
            Optional = new Optional { Value = new ProtoValue { Text = "present" } }
        };

        var result = DamlValueConverter.FromProtoValue(proto);

        result.Should().BeOfType<DamlOptional>();
        result.As<DamlOptional>().Value.Should().Be(new DamlText("present"));
    }

    [Fact]
    public void FromProtoValue_converts_optional_without_value()
    {
        var proto = new ProtoValue
        {
            Optional = new Optional { Value = null }
        };

        var result = DamlValueConverter.FromProtoValue(proto);

        result.Should().BeOfType<DamlOptional>();
        result.As<DamlOptional>().Value.Should().BeNull();
    }

    [Fact]
    public void FromProtoValue_converts_text_map()
    {
        var protoMap = new TextMap();
        protoMap.Entries.Add(new TextMap.Types.Entry { Key = "key1", Value = new ProtoValue { Text = "value1" } });
        protoMap.Entries.Add(new TextMap.Types.Entry { Key = "key2", Value = new ProtoValue { Text = "value2" } });

        var result = DamlValueConverter.FromProtoValue(new ProtoValue { TextMap = protoMap });

        result.Should().BeOfType<DamlTextMap>();
        var map = result.As<DamlTextMap>();
        map.Count.Should().Be(2);
        map["key1"].Should().Be(new DamlText("value1"));
        map["key2"].Should().Be(new DamlText("value2"));
    }

    [Fact]
    public void FromProtoValue_converts_empty_text_map()
    {
        var result = DamlValueConverter.FromProtoValue(new ProtoValue { TextMap = new TextMap() });

        result.As<DamlTextMap>().Count.Should().Be(0);
    }

    [Fact]
    public void FromProtoValue_converts_gen_map()
    {
        var protoMap = new GenMap();
        protoMap.Entries.Add(new GenMap.Types.Entry { Key = new ProtoValue { Int64 = 1 }, Value = new ProtoValue { Text = "one" } });
        protoMap.Entries.Add(new GenMap.Types.Entry { Key = new ProtoValue { Int64 = 2 }, Value = new ProtoValue { Text = "two" } });

        var result = DamlValueConverter.FromProtoValue(new ProtoValue { GenMap = protoMap });

        result.Should().BeOfType<DamlGenMap>();
        var map = result.As<DamlGenMap>();
        map.Count.Should().Be(2);
        map.Entries[0].Key.Should().Be(new DamlInt64(1));
        map.Entries[0].Value.Should().Be(new DamlText("one"));
    }

    [Fact]
    public void FromProtoValue_converts_empty_gen_map()
    {
        var result = DamlValueConverter.FromProtoValue(new ProtoValue { GenMap = new GenMap() });

        result.As<DamlGenMap>().Count.Should().Be(0);
    }

    [Fact]
    public void FromProtoValue_converts_enum()
    {
        var proto = new ProtoValue
        {
            Enum = new Com.Daml.Ledger.Api.V2.Enum
            {
                EnumId = new ProtoIdentifier { PackageId = "pkg", ModuleName = "Module", EntityName = "Color" },
                Constructor = "Red"
            }
        };

        var result = DamlValueConverter.FromProtoValue(proto);

        result.Should().BeOfType<DamlEnum>();
        var e = result.As<DamlEnum>();
        e.Constructor.Should().Be("Red");
        e.EnumId!.PackageId.Should().Be("pkg");
    }

    [Fact]
    public void FromProtoValue_converts_enum_without_id()
    {
        var proto = new ProtoValue
        {
            Enum = new Com.Daml.Ledger.Api.V2.Enum { Constructor = "Green" }
        };

        var result = DamlValueConverter.FromProtoValue(proto);

        result.As<DamlEnum>().EnumId.Should().BeNull();
        result.As<DamlEnum>().Constructor.Should().Be("Green");
    }

    [Fact]
    public void FromProtoValue_throws_for_unset_sum_case()
    {
        var action = () => DamlValueConverter.FromProtoValue(new ProtoValue());

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*SumCase.None*malformed*");
    }

    [Fact]
    public void FromProtoValue_throws_for_variant_with_null_value()
    {
        var proto = new ProtoValue
        {
            Variant = new Variant { Constructor = "Ctor" }
        };

        var action = () => DamlValueConverter.FromProtoValue(proto);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Variant*Ctor*no Value*");
    }

    [Fact]
    public void FromProtoValue_throws_for_text_map_entry_with_null_value()
    {
        var protoMap = new TextMap();
        protoMap.Entries.Add(new TextMap.Types.Entry { Key = "bad_key" });

        var action = () => DamlValueConverter.FromProtoValue(new ProtoValue { TextMap = protoMap });

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*TextMap*bad_key*no Value*");
    }

    [Fact]
    public void FromProtoValue_throws_for_text_map_with_duplicate_keys()
    {
        var protoMap = new TextMap();
        protoMap.Entries.Add(new TextMap.Types.Entry { Key = "dup", Value = new ProtoValue { Text = "first" } });
        protoMap.Entries.Add(new TextMap.Types.Entry { Key = "dup", Value = new ProtoValue { Text = "second" } });

        var action = () => DamlValueConverter.FromProtoValue(new ProtoValue { TextMap = protoMap });

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*TextMap*duplicate key 'dup'*");
    }

    [Fact]
    public void FromProtoValue_throws_for_gen_map_entry_with_null_key()
    {
        var protoMap = new GenMap();
        protoMap.Entries.Add(new GenMap.Types.Entry { Value = new ProtoValue { Text = "v" } });

        var action = () => DamlValueConverter.FromProtoValue(new ProtoValue { GenMap = protoMap });

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*GenMap*no Key*");
    }

    [Fact]
    public void FromProtoValue_throws_for_gen_map_entry_with_null_value()
    {
        var protoMap = new GenMap();
        protoMap.Entries.Add(new GenMap.Types.Entry { Key = new ProtoValue { Int64 = 1 } });

        var action = () => DamlValueConverter.FromProtoValue(new ProtoValue { GenMap = protoMap });

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*GenMap*no Value*");
    }

    [Fact]
    public void FromProtoRecord_converts_correctly()
    {
        var protoRecord = new ProtoRecord();
        protoRecord.RecordId = new ProtoIdentifier
        {
            PackageId = "pkg", ModuleName = "Module", EntityName = "Template"
        };
        protoRecord.Fields.Add(new RecordField { Label = "owner", Value = new ProtoValue { Party = "party::alice" } });
        protoRecord.Fields.Add(new RecordField { Label = "value", Value = new ProtoValue { Int64 = 100 } });

        var result = DamlValueConverter.FromProtoRecord(protoRecord);

        result.RecordId.Should().NotBeNull();
        result.RecordId!.PackageId.Should().Be("pkg");
        result.Fields.Should().HaveCount(2);
        result.Fields[0].Label.Should().Be("owner");
        result.Fields[0].Value.Should().Be(new DamlParty("party::alice"));
    }

    [Fact]
    public void FromProtoRecord_handles_null_record_id()
    {
        var protoRecord = new ProtoRecord();
        protoRecord.Fields.Add(new RecordField { Label = "field", Value = new ProtoValue { Text = "value" } });

        var result = DamlValueConverter.FromProtoRecord(protoRecord);

        result.RecordId.Should().BeNull();
        result.Fields.Should().ContainSingle();
    }

    [Fact]
    public void FromProtoRecord_handles_empty_fields()
    {
        var protoRecord = new ProtoRecord();
        protoRecord.RecordId = new ProtoIdentifier
        {
            PackageId = "pkg", ModuleName = "Module", EntityName = "Empty"
        };

        var result = DamlValueConverter.FromProtoRecord(protoRecord);

        result.RecordId!.EntityName.Should().Be("Empty");
        result.Fields.Should().BeEmpty();
    }

    [Fact]
    public void FromProtoRecord_throws_for_null_field_value()
    {
        var protoRecord = new ProtoRecord();
        protoRecord.Fields.Add(new RecordField { Label = "bad_field" });

        var action = () => DamlValueConverter.FromProtoRecord(protoRecord);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*'bad_field'*no Value*");
    }

    [Theory]
    [MemberData(nameof(RoundTripValues))]
    public void ToProtoValue_and_FromProtoValue_round_trip(DamlValue original)
    {
        var proto = DamlValueConverter.ToProtoValue(original);
        var result = DamlValueConverter.FromProtoValue(proto);

        // Re-serialize to proto and compare — avoids collection reference equality issues
        var roundTripped = DamlValueConverter.ToProtoValue(result);
        roundTripped.Should().Be(proto);
    }

    public static TheoryData<DamlValue> RoundTripValues => new()
    {
        DamlUnit.Instance,
        new DamlBool(true),
        new DamlBool(false),
        new DamlInt64(42),
        new DamlInt64(-1),
        new DamlInt64(0),
        new DamlInt64(long.MinValue),
        new DamlInt64(long.MaxValue),
        new DamlText("hello"),
        new DamlText(""),
        new DamlParty("party::alice"),
        new DamlNumeric(123.456m),
        new DamlNumeric(0m),
        new DamlNumeric(-1.23m),
        new DamlDate(new DateOnly(2024, 1, 1)),
        new DamlDate(new DateOnly(1970, 1, 1)),
        new DamlTimestamp(DateTimeOffset.UnixEpoch.AddSeconds(1704067200)),
        new DamlTimestamp(DateTimeOffset.UnixEpoch),
        new DamlContractId("00contract123"),
        new DamlRecord(
            new RuntimeIdentifier("pkg", "Mod", "Rec"),
            [new DamlField("f", new DamlText("v"))]),
        new DamlRecord(null, []),
        new DamlVariant(
            new RuntimeIdentifier("pkg", "Mod", "Var"),
            "Ctor", new DamlInt64(1)),
        new DamlVariant(null, "Left", new DamlText("x")),
        new DamlList([new DamlInt64(1), new DamlInt64(2)]),
        new DamlList([]),
        new DamlOptional(new DamlText("present")),
        new DamlOptional(null),
        new DamlTextMap(new Dictionary<string, DamlValue>
        {
            ["a"] = new DamlText("1")
        }),
        new DamlTextMap(new Dictionary<string, DamlValue>()),
        new DamlGenMap([(new DamlInt64(1), new DamlText("one"))]),
        new DamlGenMap([]),
        new DamlEnum(new RuntimeIdentifier("pkg", "Mod", "E"), "Ctor"),
        new DamlEnum(null, "Ctor"),
    };
    private sealed record UnsupportedDamlValue : DamlValue;
}
