// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class WireInt64JsonConverterTests
{
    [Fact]
    public void GetLedgerEndResponse_reads_a_numeric_offset_as_the_canonical_string()
    {
        var response = JsonSerializer.Deserialize<GetLedgerEndResponse>(
            "{\"offset\":9695}", RestRefitSettings.SerializerOptions);

        response.Should().NotBeNull();
        response.Offset.Should().Be("9695");
    }

    [Fact]
    public void GetLedgerEndResponse_still_reads_the_proto3_string_offset()
    {
        var response = JsonSerializer.Deserialize<GetLedgerEndResponse>(
            "{\"offset\":\"9695\"}", RestRefitSettings.SerializerOptions);

        response.Should().NotBeNull();
        response.Offset.Should().Be("9695");
    }

    [Fact]
    public void GetLedgerEndResponse_reads_an_offset_beyond_what_a_double_holds()
    {
        var response = JsonSerializer.Deserialize<GetLedgerEndResponse>(
            "{\"offset\":9007199254740993}", RestRefitSettings.SerializerOptions);

        response.Should().NotBeNull();
        response.Offset.Should().Be("9007199254740993");
    }

    [Fact]
    public void GetUpdatesRequest_still_sends_beginExclusive_as_the_proto3_string()
    {
        var request = new GetUpdatesRequest { BeginExclusive = "9007199254740993" };

        var json = JsonSerializer.Serialize(request, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("beginExclusive").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public void GetLedgerEndResponse_round_trips_a_numeric_offset_back_to_a_number()
    {
        var response = JsonSerializer.Deserialize<GetLedgerEndResponse>(
            "{\"offset\":9695}", RestRefitSettings.SerializerOptions);

        var json = JsonSerializer.Serialize(response, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("offset").ValueKind.Should().Be(JsonValueKind.Number);
    }

    [Fact]
    public void Completion_reads_paidTrafficCost_and_offset_from_the_same_response()
    {
        var completion = JsonSerializer.Deserialize<Completion>(
            "{\"offset\":9706,\"paidTrafficCost\":2704,\"commandId\":\"c-1\"}",
            RestRefitSettings.SerializerOptions);

        completion.Should().NotBeNull();
        completion.Offset.Should().Be("9706");
        completion.PaidTrafficCost.Should().Be("2704");
        completion.CommandId.Should().Be("c-1");
    }

    [Fact]
    public void Completion_rejects_a_commandId_sent_as_a_number()
    {
        var act = () => JsonSerializer.Deserialize<Completion>(
            "{\"commandId\":42}", RestRefitSettings.SerializerOptions);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void GetLedgerEndResponse_rejects_an_offset_that_is_neither_a_number_nor_a_string()
    {
        var act = () => JsonSerializer.Deserialize<GetLedgerEndResponse>(
            "{\"offset\":[9695]}", RestRefitSettings.SerializerOptions);

        act.Should().Throw<JsonException>().WithMessage("*int64*");
    }
}
