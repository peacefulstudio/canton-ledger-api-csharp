// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Daml.Runtime.Data;
using Xunit;
using Enum = Canton.Ledger.Rest.Client.Raw.Enum;
using Identifier = Canton.Ledger.Rest.Client.Raw.Identifier;
using Record = Canton.Ledger.Rest.Client.Raw.Record;

namespace Canton.Ledger.Rest.Client.Tests;

public class DamlLfJsonWriterTests
{
    private const long MicrosecondsAt20230406T043023 = 1680755423000000L;

    private static readonly JsonSerializerOptions DamlLfOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new WireValueJsonConverter(), new WireRecordJsonConverter(), new WireIdentifierJsonConverter() },
    };

    [Fact]
    public void WriteValue_writes_a_Party_as_a_string() => Write(new Value { Party = "alice::1220ab" }).Should().Be("\"alice::1220ab\"");

    [Fact]
    public void WriteValue_writes_a_Text_as_a_string() => Write(new Value { Text = "CH-1234" }).Should().Be("\"CH-1234\"");

    [Fact]
    public void WriteValue_writes_a_ContractId_as_a_string() => Write(new Value { ContractId = "00abcd" }).Should().Be("\"00abcd\"");

    [Fact]
    public void WriteValue_writes_true_as_a_JSON_boolean() => Write(new Value { Bool = true }).Should().Be("true");

    [Fact]
    public void WriteValue_writes_false_rather_than_omitting_it() => Write(new Value { Bool = false }).Should().Be("false");

    [Fact]
    public void WriteValue_writes_Int64_as_a_string_to_preserve_precision() =>
        Write(new Value { Int64 = "9223372036854775807" }).Should().Be("\"9223372036854775807\"");

    [Fact]
    public void WriteValue_writes_Numeric_as_a_string() => Write(new Value { Numeric = "1.5" }).Should().Be("\"1.5\"");

    [Fact]
    public void WriteValue_writes_the_epoch_Date_as_ISO_8601() => Write(new Value { Date = 0 }).Should().Be("\"1970-01-01\"");

    [Fact]
    public void WriteValue_writes_a_Date_as_ISO_8601() => Write(new Value { Date = 19453 }).Should().Be("\"2023-04-06\"");

    [Fact]
    public void WriteValue_writes_a_Date_before_the_epoch_as_ISO_8601() => Write(new Value { Date = -1 }).Should().Be("\"1969-12-31\"");

    [Fact]
    public void WriteValue_writes_a_whole_second_Timestamp_without_a_fractional_part() =>
        Write(Timestamp(MicrosecondsAt20230406T043023)).Should().Be("\"2023-04-06T04:30:23Z\"");

    [Fact]
    public void WriteValue_writes_the_microseconds_of_a_Timestamp() =>
        Write(Timestamp(MicrosecondsAt20230406T043023 + 123456)).Should().Be("\"2023-04-06T04:30:23.123456Z\"");

    [Fact]
    public void WriteValue_writes_the_epoch_Timestamp_as_ISO_8601() => Write(Timestamp(0)).Should().Be("\"1970-01-01T00:00:00Z\"");

    [Fact]
    public void WriteValue_rejects_a_Timestamp_that_is_not_a_number()
    {
        var act = () => Write(new Value { Timestamp = "2023-04-06T04:30:23Z" });

        act.Should().Throw<JsonException>().WithMessage("*timestamp*");
    }

    [Fact]
    public void WriteValue_rejects_a_Timestamp_whose_microseconds_overflow_the_tick_conversion()
    {
        var act = () => Write(Timestamp(long.MaxValue));

        act.Should().Throw<JsonException>().WithMessage("*9223372036854775807*");
    }

    [Fact]
    public void WriteValue_rejects_a_Timestamp_beyond_the_range_a_date_can_express()
    {
        var act = () => Write(Timestamp(300_000_000_000_000_000L));

        act.Should().Throw<JsonException>().WithMessage("*300000000000000000*");
    }

    [Fact]
    public void WriteValue_writes_Unit_as_an_empty_object() => Write(UnitValue()).Should().Be("{}");

    [Fact]
    public void WriteValue_writes_a_Unit_built_by_RestValueEncoder() =>
        Write(RestValueEncoder.ToWireValue(DamlUnit.Instance)).Should().Be("{}");

    [Fact]
    public void WriteValue_writes_an_empty_Optional_as_null() => Write(new Value { Optional = new Optional() }).Should().Be("null");

    [Fact]
    public void WriteValue_writes_a_present_Optional_as_the_inner_value() =>
        Write(new Value { Optional = new Optional { Value = new Value { Text = "CH-1234" } } }).Should().Be("\"CH-1234\"");

    [Fact]
    public void WriteValue_rejects_a_nested_Optional()
    {
        var value = new Value
        {
            Optional = new Optional { Value = new Value { Optional = new Optional() } },
        };

        var act = () => Write(value);

        act.Should().Throw<JsonException>().WithMessage("*nested*");
    }

    [Fact]
    public void WriteValue_writes_a_List_as_an_array()
    {
        var value = new Value
        {
            List = new List { Elements = [new Value { Text = "a" }, new Value { Text = "b" }] },
        };

        Write(value).Should().Be("[\"a\",\"b\"]");
    }

    [Fact]
    public void WriteValue_writes_a_List_with_no_elements_as_an_empty_array() =>
        Write(new Value { List = new List() }).Should().Be("[]");

    [Fact]
    public void WriteValue_writes_a_TextMap_as_an_object_keyed_by_entry_key()
    {
        var value = new Value
        {
            TextMap = new TextMap
            {
                Entries = [new TextMap_Entry { Key = "iban", Value = new Value { Text = "CH-1234" } }],
            },
        };

        Write(value).Should().Be("{\"iban\":\"CH-1234\"}");
    }

    [Fact]
    public void WriteValue_rejects_a_TextMap_entry_with_no_key()
    {
        var value = new Value
        {
            TextMap = new TextMap { Entries = [new TextMap_Entry { Value = new Value { Text = "x" } }] },
        };

        var act = () => Write(value);

        act.Should().Throw<JsonException>().WithMessage("*key*");
    }

    [Fact]
    public void WriteValue_writes_an_Enum_as_its_constructor_name() =>
        Write(new Value { Enum = new Enum { Constructor = "Green" } }).Should().Be("\"Green\"");

    [Fact]
    public void WriteValue_writes_a_Variant_as_tag_and_value()
    {
        var value = new Value
        {
            Variant = new Variant { Constructor = "InAccount", Value = new Value { Text = "CH-1234" } },
        };

        Write(value).Should().Be("{\"tag\":\"InAccount\",\"value\":\"CH-1234\"}");
    }

    [Fact]
    public void WriteValue_writes_a_Record_as_an_object_keyed_by_field_label()
    {
        var value = new Value
        {
            Record = new Record
            {
                Fields = [new RecordField { Label = "owner", Value = new Value { Party = "alice::1220ab" } }],
            },
        };

        Write(value).Should().Be("{\"owner\":\"alice::1220ab\"}");
    }

    [Fact]
    public void WriteValue_rejects_a_GenMap()
    {
        var value = new Value
        {
            GenMap = new GenMap
            {
                Entries = [new GenMap_Entry { Key = new Value { Text = "k" }, Value = new Value { Text = "v" } }],
            },
        };

        var act = () => Write(value);

        act.Should().Throw<JsonException>().WithMessage("*GenMap*");
    }

    [Fact]
    public void WriteValue_rejects_a_RecordField_with_no_label()
    {
        var value = new Value
        {
            Record = new Record { Fields = [new RecordField { Value = new Value { Text = "x" } }] },
        };

        var act = () => Write(value);

        act.Should().Throw<JsonException>().WithMessage("*label*");
    }

    [Fact]
    public void WriteValue_rejects_a_Value_with_no_arm_set()
    {
        var act = () => Write(new Value());

        act.Should().Throw<JsonException>().WithMessage("*no arm*");
    }

    [Fact]
    public void WriteRecord_writes_a_Record_with_no_fields_as_an_empty_object() =>
        WriteRecord(new Record()).Should().Be("{}");

    [Fact]
    public void WriteRecord_ignores_the_recordId_the_protobuf_shape_carries()
    {
        var record = new Record
        {
            RecordId = new Identifier { PackageId = "3557ff", ModuleName = "RichTypes", EntityName = "Marker" },
            Fields = [new RecordField { Label = "owner", Value = new Value { Party = "alice::1220ab" } }],
        };

        WriteRecord(record).Should().Be("{\"owner\":\"alice::1220ab\"}");
    }

    [Fact]
    public void WriteRecord_writes_a_nested_Record_as_a_nested_object()
    {
        var record = new Record
        {
            Fields =
            [
                new RecordField
                {
                    Label = "account",
                    Value = new Value
                    {
                        Record = new Record
                        {
                            Fields = [new RecordField { Label = "iban", Value = new Value { Text = "CH-1234" } }],
                        },
                    },
                },
            ],
        };

        WriteRecord(record).Should().Be("{\"account\":{\"iban\":\"CH-1234\"}}");
    }

    [Fact]
    public void WireRecordJsonConverter_writes_the_createArguments_of_a_CreateCommand_as_Daml_LF_JSON()
    {
        var command = new CreateCommand
        {
            TemplateId = new Identifier { PackageId = "3557ff", ModuleName = "RichTypes", EntityName = "Marker" },
            CreateArguments = new Record
            {
                Fields = [new RecordField { Label = "owner", Value = new Value { Party = "alice::1220ab" } }],
            },
        };

        var json = JsonSerializer.Serialize(command, DamlLfOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("createArguments").GetProperty("owner").GetString().Should().Be("alice::1220ab");
    }

    [Fact]
    public void WireValueJsonConverter_writes_the_choiceArgument_of_an_ExerciseCommand_as_Daml_LF_JSON()
    {
        var command = new ExerciseCommand
        {
            TemplateId = new Identifier { PackageId = "3557ff", ModuleName = "RichTypes", EntityName = "Marker" },
            ContractId = "00abcd",
            Choice = "Archive",
            ChoiceArgument = RestValueEncoder.ToWireValue(DamlUnit.Instance),
        };

        var json = JsonSerializer.Serialize(command, DamlLfOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("choiceArgument").GetRawText().Should().Be("{}");
    }

    [Fact]
    public void WireRecordJsonConverter_omits_a_null_Record_rather_than_encoding_it()
    {
        var command = new CreateCommand
        {
            TemplateId = new Identifier { PackageId = "3557ff", ModuleName = "RichTypes", EntityName = "Marker" },
        };

        var json = JsonSerializer.Serialize(command, DamlLfOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.TryGetProperty("createArguments", out _).Should().BeFalse();
    }

    [Fact]
    public void WireValueJsonConverter_reads_a_Value_in_the_shape_our_specification_declares()
    {
        var value = JsonSerializer.Deserialize<Value>("""{"party":"alice::1220ab"}""", DamlLfOptions);

        value!.Party.Should().Be("alice::1220ab");
    }

    [Fact]
    public void WireRecordJsonConverter_reads_a_Record_whose_nested_Value_is_also_left_unconverted()
    {
        var record = JsonSerializer.Deserialize<Record>(
            """{"fields":[{"label":"owner","value":{"party":"alice::1220ab"}}]}""",
            DamlLfOptions);

        record!.Fields.Should().ContainSingle();
        record.Fields.Single().Label.Should().Be("owner");
        record.Fields.Single().Value.Party.Should().Be("alice::1220ab");
    }

    [Fact]
    public void WireValueJsonConverter_translates_a_null_Value_into_a_JsonException()
    {
        var act = () => WriteThrough(writer => new WireValueJsonConverter().Write(writer, null!, DamlLfOptions));

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void WireRecordJsonConverter_translates_a_null_Record_into_a_JsonException()
    {
        var act = () => WriteThrough(writer => new WireRecordJsonConverter().Write(writer, null!, DamlLfOptions));

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void WireValueJsonConverter_translates_a_null_writer_into_a_JsonException()
    {
        var act = () => new WireValueJsonConverter().Write(null!, new Value { Text = "x" }, DamlLfOptions);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void WireRecordJsonConverter_translates_a_null_writer_into_a_JsonException()
    {
        var act = () => new WireRecordJsonConverter().Write(null!, new Record(), DamlLfOptions);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void CreateCommand_still_writes_a_payload_Int64_as_a_string_through_the_registered_options()
    {
        var command = new CreateCommand
        {
            TemplateId = new Identifier { PackageId = "3557ff", ModuleName = "RichTypes", EntityName = "RichRecord" },
            CreateArguments = new Record
            {
                Fields = [new RecordField { Label = "count", Value = new Value { Int64 = "9007199254740993" } }],
            },
        };

        var json = JsonSerializer.Serialize(command, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        var count = document.RootElement.GetProperty("createArguments").GetProperty("count");
        count.ValueKind.Should().Be(JsonValueKind.String);
        count.GetString().Should().Be("9007199254740993");
    }

    private static Value Timestamp(long microsecondsSinceEpoch) =>
        new() { Timestamp = microsecondsSinceEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture) };

    private static Value UnitValue()
    {
        var value = new Value();
        value.AdditionalProperties["unit"] = new object();
        return value;
    }

    private static string Write(Value value) => WriteThrough(writer => DamlLfJsonWriter.WriteValue(writer, value));

    private static string WriteRecord(Record record) => WriteThrough(writer => DamlLfJsonWriter.WriteRecord(writer, record));

    private static string WriteThrough(Action<Utf8JsonWriter> write)
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
