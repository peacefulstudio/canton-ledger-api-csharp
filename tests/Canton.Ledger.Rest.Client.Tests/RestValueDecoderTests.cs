// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Data;
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
    public void ToDamlValue_leaves_a_sum_case_it_does_not_recognise_named_as_a_malformed_response()
    {
        var thrown = Record.Exception(() => RestValueDecoder.ToDamlValue(new WireValue()));

        thrown.Should().BeOfType<MalformedResponseException>()
            .Which.Message.Should().Be(
                "Malformed response from ledger: Received a wire Value with no recognisable sum case set.");
    }
}
