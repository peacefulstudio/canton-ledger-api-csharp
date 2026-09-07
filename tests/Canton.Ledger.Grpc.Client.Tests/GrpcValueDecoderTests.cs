// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Google.Protobuf.WellKnownTypes;
using Xunit;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoField = Com.Daml.Ledger.Api.V2.RecordField;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;

namespace Canton.Ledger.Grpc.Client.Tests;

public class GrpcValueDecoderTests
{
    [Fact]
    public void ToPayload_returns_the_wire_record_rendered_as_json()
    {
        var record = new ProtoRecord();
        record.Fields.Add(new ProtoField { Label = "amount", Value = new ProtoValue { Int64 = 7 } });

        GrpcValueDecoder.ToPayload(record).Should().Contain("amount");
    }

    [Fact]
    public void ToPayload_blames_the_participant_when_the_record_cannot_be_rendered()
    {
        var record = new ProtoRecord();
        record.Fields.Add(new ProtoField { Label = "note", Value = new ProtoValue { Text = "\udc00" } });

        var thrown = Record.Exception(() => GrpcValueDecoder.ToPayload(record));

        thrown.Should().BeOfType<MalformedResponseException>()
            .Which.Message.Should().Be(
                "Malformed response from ledger: String contains high surrogate not preceded by low surrogate");
        thrown.InnerException.Should().BeOfType<ArgumentException>();
    }

    [Fact]
    public void ToCreatedAt_returns_null_when_the_created_event_states_no_creation_time()
    {
        GrpcValueDecoder.ToCreatedAt(new ProtoCreatedEvent { ContractId = "00aa" }).Should().BeNull();
    }

    [Fact]
    public void ToCreatedAt_reads_a_normalised_wire_timestamp()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00aa",
            CreatedAt = new Timestamp { Seconds = 1_700_000_000 },
        };

        GrpcValueDecoder.ToCreatedAt(created).Should().Be(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
    }

    [Fact]
    public void ToCreatedAt_blames_the_participant_when_the_wire_timestamp_is_unnormalised()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00aa",
            CreatedAt = new Timestamp { Seconds = 0, Nanos = -1 },
        };

        var thrown = Record.Exception(() => GrpcValueDecoder.ToCreatedAt(created));

        thrown.Should().BeOfType<MalformedResponseException>()
            .Which.Message.Should().StartWith(
                "Malformed response from ledger: Timestamp contains invalid values");
        thrown.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public void ToDamlRecord_blames_the_participant_when_a_wire_date_falls_outside_the_Daml_LF_range()
    {
        var record = new ProtoRecord();
        record.Fields.Add(new ProtoField { Label = "when", Value = new ProtoValue { Date = int.MaxValue } });

        var thrown = Record.Exception(() => GrpcValueDecoder.ToDamlRecord(record));

        thrown.Should().BeOfType<MalformedResponseException>()
            .Which.Message.Should().StartWith(
                "Malformed response from ledger: Days since epoch must resolve to a date within the Daml-LF Date range");
        thrown.InnerException.Should().BeOfType<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToDamlValue_blames_the_participant_when_the_wire_value_sets_no_sum_case()
    {
        var thrown = Record.Exception(() => GrpcValueDecoder.ToDamlValue(new ProtoValue()));

        thrown.Should().BeOfType<MalformedResponseException>()
            .Which.Message.Should().StartWith(
                "Malformed response from ledger: Received a proto Value with no value set (SumCase.None).");
        thrown.InnerException.Should().BeOfType<InvalidOperationException>();
    }
}
