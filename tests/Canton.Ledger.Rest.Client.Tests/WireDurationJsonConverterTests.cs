// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json;
using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;
using Record = Canton.Ledger.Rest.Client.Raw.Record;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class WireDurationJsonConverterTests
{
    [Theory]
    [InlineData("3600s", 3600, 0)]
    [InlineData("0s", 0, 0)]
    [InlineData("1.5s", 1, 500_000_000)]
    [InlineData("0.000000001s", 0, 1)]
    [InlineData("-1.5s", -1, -500_000_000)]
    public void Commands_sends_minLedgerTimeRel_as_a_duration_object(string bound, long seconds, int nanos)
    {
        var json = JsonSerializer.Serialize(
            new Commands { MinLedgerTimeRel = bound }, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        var written = document.RootElement.GetProperty("minLedgerTimeRel");
        written.ValueKind.Should().Be(JsonValueKind.Object);
        written.GetProperty("seconds").GetInt64().Should().Be(seconds);
        written.GetProperty("nanos").GetInt32().Should().Be(nanos);
    }

    [Theory]
    [InlineData("totally-not-a-duration")]
    [InlineData("3600")]
    [InlineData("")]
    [InlineData("1.0000000001s")]
    [InlineData("PT1H")]
    public void Commands_refuses_to_send_a_minLedgerTimeRel_that_is_not_a_duration(string garbage)
    {
        var act = () => JsonSerializer.Serialize(
            new Commands { MinLedgerTimeRel = garbage }, RestRefitSettings.SerializerOptions);

        act.Should().Throw<JsonException>().WithMessage($"*{garbage}*");
    }

    [Fact]
    public void Commands_omits_minLedgerTimeRel_when_no_bound_is_set()
    {
        var json = JsonSerializer.Serialize(
            new Commands { CommandId = "no-bound" }, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.TryGetProperty("minLedgerTimeRel", out _).Should().BeFalse();
    }

    [Fact]
    public void Commands_still_sends_minLedgerTimeAbs_as_a_flat_string()
    {
        var json = JsonSerializer.Serialize(
            new Commands { MinLedgerTimeAbs = DateTimeOffset.Parse("2026-08-26T12:00:00Z", CultureInfo.InvariantCulture) },
            RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("minLedgerTimeAbs").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Theory]
    [InlineData("{\"seconds\":3600,\"nanos\":0}", "3600s")]
    [InlineData("{\"seconds\":\"3600\",\"nanos\":0}", "3600s")]
    [InlineData("{\"seconds\":1,\"nanos\":500000000}", "1.500s")]
    [InlineData("{\"seconds\":-1,\"nanos\":-500000000}", "-1.500s")]
    [InlineData("{\"seconds\":0,\"nanos\":1}", "0.000000001s")]
    public void Commands_reads_a_duration_object_back_as_the_canonical_string(string served, string bound)
    {
        var commands = JsonSerializer.Deserialize<Commands>(
            $"{{\"minLedgerTimeRel\":{served}}}", RestRefitSettings.SerializerOptions);

        commands.Should().NotBeNull();
        commands.MinLedgerTimeRel.Should().Be(bound);
    }

    [Fact]
    public void Commands_still_reads_the_proto3_string_form_of_minLedgerTimeRel()
    {
        var commands = JsonSerializer.Deserialize<Commands>(
            "{\"minLedgerTimeRel\":\"3600s\"}", RestRefitSettings.SerializerOptions);

        commands.Should().NotBeNull();
        commands.MinLedgerTimeRel.Should().Be("3600s");
    }

    [Fact]
    public void Commands_rejects_a_minLedgerTimeRel_that_is_neither_an_object_nor_a_string()
    {
        var act = () => JsonSerializer.Deserialize<Commands>(
            "{\"minLedgerTimeRel\":[3600]}", RestRefitSettings.SerializerOptions);

        act.Should().Throw<JsonException>().WithMessage("*duration*");
    }

    [Fact]
    public void Commands_names_the_offending_pair_when_a_served_nanos_is_the_smallest_long()
    {
        var act = () => JsonSerializer.Deserialize<Commands>(
            "{\"minLedgerTimeRel\":{\"seconds\":0,\"nanos\":-9223372036854775808}}",
            RestRefitSettings.SerializerOptions);

        act.Should().Throw<JsonException>()
            .WithMessage("*alongside nanos -9223372036854775808*")
            .Which.InnerException.Should().BeNull(
                "the range guard has to reject this pair itself; deciding it by taking an absolute "
                + "value overflows on the smallest long, and the converter would report the vaguer "
                + "'could not be read' with the arithmetic failure attached as the cause");
    }

    [Fact]
    public void Commands_names_the_offending_pair_when_a_served_seconds_is_the_smallest_long()
    {
        var act = () => JsonSerializer.Deserialize<Commands>(
            "{\"minLedgerTimeRel\":{\"seconds\":-9223372036854775808,\"nanos\":0}}",
            RestRefitSettings.SerializerOptions);

        act.Should().Throw<JsonException>()
            .WithMessage("*seconds -9223372036854775808*")
            .Which.InnerException.Should().BeNull(
                "no canonical string can carry this magnitude — rendering it would emit a duration "
                + "this same codec refuses to parse back — so the pair has to be rejected outright "
                + "rather than reaching the arithmetic that overflows on it");
    }

    [Fact]
    public void Commands_round_trips_a_served_duration_object_back_to_an_object()
    {
        var commands = JsonSerializer.Deserialize<Commands>(
            "{\"minLedgerTimeRel\":{\"seconds\":3600,\"nanos\":0}}", RestRefitSettings.SerializerOptions);

        var json = JsonSerializer.Serialize(commands, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("minLedgerTimeRel").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task SubmitAndWait_puts_the_bound_on_the_wire_as_a_duration_object()
    {
        var (api, transport) = RestApiFactory.Build<ICommandServiceApi>();
        transport.WithResponse(HttpStatusCode.OK, "{\"updateId\":\"u-1\",\"completionOffset\":9695}");

        await api.SubmitAndWait(
            new Commands { CommandId = "c-1", MinLedgerTimeRel = "3600s" },
            TestContext.Current.CancellationToken);

        transport.LastRequestBody.Should()
            .Contain("\"minLedgerTimeRel\":{\"seconds\":3600,\"nanos\":0}")
            .And.NotContain("\"3600s\"");
    }

    [Fact]
    public void A_Daml_payload_string_that_looks_like_a_duration_is_left_alone()
    {
        var command = new CreateCommand
        {
            CreateArguments = new Record
            {
                Fields = [new RecordField { Label = "label", Value = new Value { Text = "3600s" } }],
            },
        };

        var json = JsonSerializer.Serialize(command, RestRefitSettings.SerializerOptions);

        json.Should().Contain("3600s");
    }
}
