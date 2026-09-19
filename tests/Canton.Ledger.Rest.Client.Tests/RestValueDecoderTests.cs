// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;
using WireRecord = Canton.Ledger.Rest.Client.Raw.Record;
using WireValue = Canton.Ledger.Rest.Client.Raw.Value;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestValueDecoderTests
{
    [Fact]
    public void ToDamlValue_reads_a_date_inside_the_Daml_LF_range()
    {
        RestValueDecoder.ToDamlValue(new WireValue { Date = 0 })
            .Should().BeOfType<DamlDate>();
    }

    [Fact]
    public void ToDamlValue_blames_the_participant_when_a_wire_date_falls_outside_the_Daml_LF_range()
    {
        var thrown = Record.Exception(() => RestValueDecoder.ToDamlValue(new WireValue { Date = int.MaxValue }));

        thrown.Should().BeOfType<MalformedResponseException>()
            .Which.Message.Should().StartWith(
                "Malformed response from ledger: Days since epoch must resolve to a date within the Daml-LF Date range");
        thrown.InnerException.Should().BeOfType<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToDamlValue_blames_the_participant_when_a_wire_timestamp_falls_outside_the_Daml_LF_range()
    {
        var thrown = Record.Exception(() => RestValueDecoder.ToDamlValue(
            new WireValue { Timestamp = "9223372036854775807" }));

        thrown.Should().BeOfType<MalformedResponseException>()
            .Which.Message.Should().StartWith(
                "Malformed response from ledger: Microseconds since epoch must resolve to a timestamp within the "
                + "Daml-LF Timestamp range");
        thrown.InnerException.Should().BeOfType<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToDamlRecord_normalises_an_offset_bearing_idiomatic_Time_to_UTC()
    {
        var record = new WireRecord();
        record.AdditionalProperties["at"] = "2026-09-02T12:00:00+02:00";

        var decoded = (DateTimeOffset)RestValueDecoder.ToDamlRecord(record)
            .Fields.Should().ContainSingle().Subject.Value
            .Should().BeOfType<DamlTimestamp>().Subject;

        decoded.Offset.Should().Be(TimeSpan.Zero, "every REST-decoded Time now reads UTC");
        decoded.Hour.Should().Be(10);
    }

    [Fact]
    public void ToDamlValue_leaves_an_idiomatic_sum_case_it_does_not_recognise_named_as_a_malformed_response()
    {
        var value = new WireValue();
        value.AdditionalProperties["mystery"] = "??";

        var thrown = Record.Exception(() => RestValueDecoder.ToDamlValue(value));

        thrown.Should().BeOfType<MalformedResponseException>()
            .Which.Message.Should().Be(
                "Malformed response from ledger: Received a wire Value with no recognisable sum case set.");
    }

    [Fact]
    public void ToDamlValue_applies_WireUnitEncoding_to_a_bare_empty_object_the_untyped_path_cannot_resolve()
    {
        RestValueDecoder.ToDamlValue(new WireValue())
            .Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void ToDamlValue_reads_the_wire_tagged_unit_arm_without_reaching_WireUnitEncoding()
    {
        var value = new WireValue();
        value.AdditionalProperties[WireValueNames.Unit] = new object();

        RestValueDecoder.ToDamlValue(value).Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void ToDamlValueOfT_reads_a_bare_empty_object_as_the_empty_record_the_choice_returns()
    {
        var decoded = RestValueDecoder.ToDamlValue<CirceReceipt>(new WireValue());

        decoded.Should().BeOfType<DamlRecord>()
            .Which.Fields.Should().BeEmpty();
    }

    [Fact]
    public void ToDamlValueOfT_still_reads_a_bare_empty_object_as_Unit_when_the_choice_returns_Unit()
    {
        RestValueDecoder.ToDamlValue<DamlUnit>(new WireValue())
            .Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void ToDamlValueOfT_falls_back_to_WireUnitEncoding_when_the_choice_returns_a_deferred_generic_family()
    {
        RestValueDecoder.ToDamlValue<IReadOnlyDictionary<string, string>>(new WireValue())
            .Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void ToDamlValueOfType_reads_a_bare_empty_object_as_the_empty_record_the_choice_returns()
    {
        var reflectivelyKnownType = typeof(CirceReceipt);

        var decoded = RestValueDecoder.ToDamlValue(new WireValue(), reflectivelyKnownType);

        decoded.Should().BeOfType<DamlRecord>()
            .Which.Fields.Should().BeEmpty();
    }

    [Fact]
    public void ToDamlValueOfType_still_reads_a_bare_empty_object_as_Unit_when_the_choice_returns_Unit()
    {
        var reflectivelyKnownType = typeof(DamlUnit);

        RestValueDecoder.ToDamlValue(new WireValue(), reflectivelyKnownType)
            .Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void ToDamlValueOfType_falls_back_to_WireUnitEncoding_when_the_choice_returns_a_deferred_generic_family()
    {
        var reflectivelyKnownType = typeof(IReadOnlyDictionary<string, string>);

        RestValueDecoder.ToDamlValue(new WireValue(), reflectivelyKnownType)
            .Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void ToDamlValueOfT_reads_an_idiomatic_record_shaped_Value_against_the_requested_type()
    {
        var value = new WireValue();
        value.AdditionalProperties["owner"] = "alice::ns1";
        value.AdditionalProperties["amount"] = "10.5";

        var decoded = RestValueDecoder.ToDamlValue<CirceMarker>(value);

        var record = decoded.Should().BeOfType<DamlRecord>().Subject;
        record.GetRequiredField("owner").As<DamlParty>().Value.Should().Be("alice::ns1");
        record.GetRequiredField("amount").As<DamlNumeric>().Value.Should().Be(10.5m);
    }

    [Fact]
    public void ToDamlValueOfT_falls_back_to_the_untyped_reader_when_the_requested_type_is_a_deferred_generic_family()
    {
        var value = new WireValue();
        value.AdditionalProperties["mystery"] = "??";

        var thrown = Record.Exception(() => RestValueDecoder.ToDamlValue<IReadOnlyList<string>>(value));

        thrown.Should().BeOfType<MalformedResponseException>()
            .Which.Message.Should().Be(
                "Malformed response from ledger: Received a wire Value with no recognisable sum case set.");
    }

    [Fact]
    public void ToDamlValueOfT_reads_a_bare_scalar_wire_Value_against_the_requested_Party_type()
    {
        var value = JsonSerializer.Deserialize<WireValue>(
            "\"alice::1220ab\"", RestRefitSettings.SerializerOptions)!;

        var decoded = RestValueDecoder.ToDamlValue<Party>(value);

        decoded.Should().Be(new DamlParty("alice::1220ab"));
    }

    [Fact]
    public void ToDamlValueOfT_throws_loudly_when_the_requested_type_maps_to_nothing_in_Daml_LF()
    {
        var value = new WireValue();
        value.AdditionalProperties["mystery"] = "??";

        var thrown = Record.Exception(() => RestValueDecoder.ToDamlValue<object>(value));

        var malformed = thrown.Should().BeOfType<MalformedResponseException>().Subject;
        malformed.InnerException.Should().BeOfType<NotSupportedException>()
            .Which.Message.Should().Contain("lies outside the Daml type mapping for a top-level value");
    }

    [Fact]
    public void DeferredGenericFamilyMessageFragment_still_matches_upstream_exception()
    {
        // Pins the Daml.Runtime message text RestValueDecoder uses to distinguish deferred generics from
        // unmapped types — if this fails at a Daml repin, update DeferredGenericFamilyMessageFragment.
        var thrown = Record.Exception(() =>
            DamlLfJsonReader.ReadValue<IReadOnlyList<string>>(JsonDocument.Parse("[]").RootElement));
        thrown.Should().BeOfType<NotSupportedException>()
            .Which.Message.Should().Contain("generic Daml type family");
    }

    [Fact]
    public void ToDamlValueOfType_reads_a_bare_scalar_wire_Value_against_a_reflectively_known_Party_type()
    {
        var value = JsonSerializer.Deserialize<WireValue>(
            "\"alice::1220ab\"", RestRefitSettings.SerializerOptions)!;
        var reflectivelyKnownType = typeof(Party);

        var decoded = RestValueDecoder.ToDamlValue(value, reflectivelyKnownType);

        decoded.Should().Be(new DamlParty("alice::1220ab"));
    }

    [Fact]
    public void ToDamlValueOfType_reads_an_idiomatic_record_shaped_Value_against_a_reflectively_known_type()
    {
        var value = new WireValue();
        value.AdditionalProperties["owner"] = "alice::ns1";
        value.AdditionalProperties["amount"] = "10.5";
        var reflectivelyKnownType = typeof(CirceMarker);

        var decoded = RestValueDecoder.ToDamlValue(value, reflectivelyKnownType);

        var record = decoded.Should().BeOfType<DamlRecord>().Subject;
        record.GetRequiredField("owner").As<DamlParty>().Value.Should().Be("alice::ns1");
        record.GetRequiredField("amount").As<DamlNumeric>().Value.Should().Be(10.5m);
    }

    [Fact]
    public void ToDamlValueOfType_leaves_an_idiomatic_sum_case_it_does_not_recognise_named_as_a_malformed_response()
    {
        var value = new WireValue();
        value.AdditionalProperties["mystery"] = "??";
        var reflectivelyKnownType = typeof(IReadOnlyList<string>);

        var thrown = Record.Exception(() => RestValueDecoder.ToDamlValue(value, reflectivelyKnownType));

        thrown.Should().BeOfType<MalformedResponseException>()
            .Which.Message.Should().Be(
                "Malformed response from ledger: Received a wire Value with no recognisable sum case set.");
    }
}
