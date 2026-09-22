// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;
using WireRecord = Canton.Ledger.Rest.Client.Raw.Record;
using WireValue = Canton.Ledger.Rest.Client.Raw.Value;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestValueDecoderTests
{
    private sealed record AtRecord([property: DamlFieldAttribute("at")] DateTimeOffset At) : IDamlRecord<AtRecord>
    {
        public DamlRecord ToRecord() => throw new NotSupportedException();
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context, ("at", DamlLfJsonDecoders.ReadTimestamp));

        public static AtRecord FromRecord(DamlRecord record) => throw new NotSupportedException();
    }

    [Fact]
    public void ToDamlValue_reads_a_date_inside_the_Daml_LF_range()
    {
        RestValueDecoder.ToDamlValue(WireValueOf("\"1970-01-01\""), DamlLfJsonDecoders.ReadDate, "date")
            .Should().Be(new DamlDate(new DateOnly(1970, 1, 1)));
    }

    [Fact]
    public void ToDamlValue_blames_the_participant_when_a_wire_date_falls_outside_the_Daml_LF_range()
    {
        var thrown = Record.Exception(() => RestValueDecoder.ToDamlValue(WireValueOf("\"10000-01-01\""), DamlLfJsonDecoders.ReadDate, "date"));

        thrown.Should().BeOfType<MalformedResponseException>()
            .Which.Message.Should().StartWith("Malformed response from ledger: ")
            .And.Contain("'10000-01-01' at 'date' is not a valid Daml Date");
        thrown.InnerException.Should().BeOfType<JsonException>();
    }

    [Fact]
    public void ToDamlValue_blames_the_participant_when_a_wire_timestamp_falls_outside_the_Daml_LF_range()
    {
        var thrown = Record.Exception(() => RestValueDecoder.ToDamlValue(
            WireValueOf("\"0000-12-31T23:59:59Z\""), DamlLfJsonDecoders.ReadTimestamp, "timestamp"));

        thrown.Should().BeOfType<MalformedResponseException>()
            .Which.Message.Should().StartWith("Malformed response from ledger: ")
            .And.Contain("is not a valid Daml Timestamp");
        thrown.InnerException.Should().BeOfType<JsonException>();
    }

    [Fact]
    public void ToDamlRecord_normalises_an_offset_bearing_Time_to_UTC()
    {
        var record = JsonSerializer.Deserialize<WireRecord>(
            """{"at": "2026-09-02T12:00:00+02:00"}""", RestRefitSettings.SerializerOptions)!;

        var decoded = (DateTimeOffset)RestValueDecoder.ToDamlRecord<AtRecord>(record)
            .Fields.Should().ContainSingle().Subject.Value
            .Should().BeOfType<DamlTimestamp>().Subject;

        decoded.Offset.Should().Be(TimeSpan.Zero, "every REST-decoded Time reads UTC");
        decoded.Hour.Should().Be(10);
    }

    [Fact]
    public void ToDamlValue_reads_a_bare_empty_object_as_the_empty_record_the_type_declares()
    {
        RestValueDecoder.ToDamlValue(WireValueOf("{}"), CirceReceipt.__ReadDamlLfJson, "receipt")
            .Should().BeOfType<DamlRecord>()
            .Which.Fields.Should().BeEmpty();
    }

    [Fact]
    public void ToDamlValue_reads_a_bare_empty_object_as_Unit_when_the_type_is_Unit()
    {
        RestValueDecoder.ToDamlValue(WireValueOf("{}"), DamlLfJsonDecoders.ReadUnit, "unit")
            .Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void ToDamlValue_reads_a_bare_empty_object_as_the_empty_TextMap_when_the_reader_is_a_TextMap_reader()
    {
        RestValueDecoder.ToDamlValue(WireValueOf("{}"), TextMapOfText, "map")
            .Should().BeOfType<DamlTextMap>()
            .Which.Values.Should().BeEmpty();
    }

    [Fact]
    public void ToDamlValue_reads_a_bare_empty_array_as_the_empty_list_when_the_reader_is_a_list_reader()
    {
        RestValueDecoder.ToDamlValue(WireValueOf("[]"), ListOfText, "list")
            .Should().BeOfType<DamlList>()
            .Which.Values.Should().BeEmpty();
    }

    [Fact]
    public void ToDamlValue_reads_a_record_shaped_Value_against_the_requested_type()
    {
        var decoded = RestValueDecoder.ToDamlValue(
            WireValueOf("""{"owner": "alice::ns1", "amount": "10.5"}"""), CirceMarker.__ReadDamlLfJson, "marker");

        var record = decoded.Should().BeOfType<DamlRecord>().Subject;
        record.GetRequiredField("owner").Should().Be(new DamlParty("alice::ns1"));
        record.GetRequiredField("amount").Should().Be(new DamlNumeric(10.5m));
    }

    [Fact]
    public void ToDamlValue_reads_a_bare_scalar_wire_Value_as_the_Party_the_type_names_rather_than_as_Text()
    {
        RestValueDecoder.ToDamlValue(WireValueOf("\"alice::1220ab\""), DamlLfJsonDecoders.ReadParty, "owner")
            .Should().Be(new DamlParty("alice::1220ab"));
    }

    [Fact]
    public void ToDamlValue_reads_the_same_bare_scalar_as_Text_when_the_type_is_Text()
    {
        RestValueDecoder.ToDamlValue(WireValueOf("\"alice::1220ab\""), DamlLfJsonDecoders.ReadText, "label")
            .Should().Be(new DamlText("alice::1220ab"));
    }

    [Fact]
    public void ToDamlValue_throws_loudly_when_the_reader_stands_for_a_type_outside_the_Daml_LF_mapping()
    {
        var thrown = Record.Exception(() => RestValueDecoder.ToDamlValue(
            WireValueOf("""{"mystery": "??"}"""),
            (json, context) => DamlLfJsonDecoders.ReadUnsupported(json, context, "System.Object"),
            "mystery"));

        thrown.Should().BeOfType<MalformedResponseException>()
            .Which.InnerException.Should().BeOfType<NotSupportedException>();
    }

    [Fact]
    public void ToDamlRecord_refuses_a_hand_built_record_that_carries_no_Daml_LF_JSON()
    {
        var thrown = Record.Exception(() => RestValueDecoder.ToDamlRecord<CirceReceipt>(new WireRecord()));

        thrown.Should().BeOfType<MalformedResponseException>()
            .Which.InnerException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be(
                "the value carries no Daml-LF JSON under 'idiomatic'; only a value read from a participant response can be decoded.");
    }

    [Fact]
    public void WireValue_reads_an_explicit_json_null_as_raw_Daml_LF_JSON_null()
    {
        var value = WireValueOf("null");

        value.AdditionalProperties.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<string, object>("idiomatic", "null"));
    }

    private static DamlValue TextMapOfText(JsonElement json, DamlLfJsonDecodeContext context) =>
        DamlLfJsonDecoders.ReadTextMap(json, context, DamlLfJsonDecoders.ReadText);

    private static DamlValue ListOfText(JsonElement json, DamlLfJsonDecodeContext context) =>
        DamlLfJsonDecoders.ReadList(json, context, DamlLfJsonDecoders.ReadText);

    private static WireValue WireValueOf(string lfJson) =>
        JsonSerializer.Deserialize<WireValue>(lfJson, RestRefitSettings.SerializerOptions)!;
}
