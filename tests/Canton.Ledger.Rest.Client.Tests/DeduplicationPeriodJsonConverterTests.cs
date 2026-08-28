// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class DeduplicationPeriodJsonConverterTests
{
    [Fact]
    public void Write_wraps_the_offset_in_a_value_object_holding_a_number()
    {
        var period = new DeduplicationPeriod { DeduplicationOffset = "42" };

        var json = JsonSerializer.Serialize(period, RestRefitSettings.SerializerOptions);

        json.Should().Be("{\"DeduplicationOffset\":{\"value\":42}}");
    }

    [Fact]
    public void Write_preserves_precision_beyond_what_a_double_holds()
    {
        var period = new DeduplicationPeriod { DeduplicationOffset = "9007199254740993" };

        var json = JsonSerializer.Serialize(period, RestRefitSettings.SerializerOptions);

        json.Should().Be("{\"DeduplicationOffset\":{\"value\":9007199254740993}}");
    }

    [Fact]
    public void Write_writes_the_offset_as_a_number_inside_a_Commands_payload()
    {
        var commands = new Commands
        {
            DeduplicationPeriod = new DeduplicationPeriod { DeduplicationOffset = "42" },
        };

        var json = JsonSerializer.Serialize(commands, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        var offset = document.RootElement
            .GetProperty("deduplicationPeriod")
            .GetProperty("DeduplicationOffset")
            .GetProperty("value");
        offset.ValueKind.Should().Be(JsonValueKind.Number);
        offset.GetInt64().Should().Be(42);
    }

    [Theory]
    [InlineData("30s", 30, 0)]
    [InlineData("1.5s", 1, 500000000)]
    [InlineData("1.500s", 1, 500000000)]
    [InlineData("0.000000001s", 0, 1)]
    [InlineData("0s", 0, 0)]
    [InlineData("-1.5s", -1, -500000000)]
    public void Write_wraps_the_duration_in_a_value_object_holding_seconds_and_nanos(
        string duration, long expectedSeconds, int expectedNanos)
    {
        var period = new DeduplicationPeriod { DeduplicationDuration = duration };

        var json = JsonSerializer.Serialize(period, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        var value = document.RootElement.GetProperty("DeduplicationDuration").GetProperty("value");
        value.GetProperty("seconds").GetInt64().Should().Be(expectedSeconds);
        value.GetProperty("nanos").GetInt32().Should().Be(expectedNanos);
    }

    [Theory]
    [InlineData("30")]
    [InlineData("30 seconds")]
    [InlineData("PT30S")]
    [InlineData("1.0000000005s")]
    public void Write_rejects_a_duration_that_is_not_a_protobuf_duration(string duration)
    {
        var period = new DeduplicationPeriod { DeduplicationDuration = duration };

        var act = () => JsonSerializer.Serialize(period, RestRefitSettings.SerializerOptions);

        act.Should().Throw<JsonException>().WithMessage($"*{duration}*");
    }

    [Fact]
    public void Write_rejects_an_offset_that_is_not_an_integer()
    {
        var period = new DeduplicationPeriod { DeduplicationOffset = "not-an-offset" };

        var act = () => JsonSerializer.Serialize(period, RestRefitSettings.SerializerOptions);

        act.Should().Throw<JsonException>().WithMessage("*not-an-offset*");
    }

    [Fact]
    public void Read_returns_a_wrapped_numeric_offset_as_the_canonical_string()
    {
        var period = JsonSerializer.Deserialize<DeduplicationPeriod>(
            "{\"DeduplicationOffset\":{\"value\":9007199254740993}}", RestRefitSettings.SerializerOptions);

        period.Should().NotBeNull();
        period!.DeduplicationOffset.Should().Be("9007199254740993");
    }

    [Fact]
    public void Read_accepts_a_bare_numeric_offset()
    {
        var period = JsonSerializer.Deserialize<DeduplicationPeriod>(
            "{\"DeduplicationOffset\":42}", RestRefitSettings.SerializerOptions);

        period.Should().NotBeNull();
        period!.DeduplicationOffset.Should().Be("42");
        period.AdditionalProperties.Should().NotContainKey("DeduplicationOffset");
    }

    [Fact]
    public void Read_accepts_the_offset_shape_our_own_specification_declares()
    {
        var period = JsonSerializer.Deserialize<DeduplicationPeriod>(
            "{\"DeduplicationOffset\":\"42\"}", RestRefitSettings.SerializerOptions);

        period.Should().NotBeNull();
        period!.DeduplicationOffset.Should().Be("42");
    }

    [Theory]
    [InlineData("{\"value\":{\"seconds\":30,\"nanos\":0}}", "30s")]
    [InlineData("{\"value\":{\"seconds\":1,\"nanos\":500000000}}", "1.500s")]
    [InlineData("{\"value\":{\"seconds\":0,\"nanos\":123456789}}", "0.123456789s")]
    [InlineData("{\"value\":{\"seconds\":\"30\",\"nanos\":0}}", "30s")]
    [InlineData("{\"value\":{\"seconds\":30}}", "30s")]
    [InlineData("{\"seconds\":30,\"nanos\":0}", "30s")]
    [InlineData("{\"value\":{\"seconds\":-1,\"nanos\":-500000000}}", "-1.500s")]
    public void Read_returns_the_duration_the_participant_serves_as_the_canonical_string(
        string served, string expected)
    {
        var period = JsonSerializer.Deserialize<DeduplicationPeriod>(
            $"{{\"DeduplicationDuration\":{served}}}", RestRefitSettings.SerializerOptions);

        period.Should().NotBeNull();
        period!.DeduplicationDuration.Should().Be(expected);
        period.DeduplicationOffset.Should().BeNull();
        period.AdditionalProperties.Should().NotContainKey("DeduplicationDuration");
    }

    [Fact]
    public void Read_returns_the_duration_arm_unchanged()
    {
        var period = JsonSerializer.Deserialize<DeduplicationPeriod>(
            "{\"DeduplicationDuration\":\"30s\"}", RestRefitSettings.SerializerOptions);

        period.Should().NotBeNull();
        period!.DeduplicationDuration.Should().Be("30s");
        period.DeduplicationOffset.Should().BeNull();
        period.AdditionalProperties.Should().NotContainKey("DeduplicationDuration");
    }

    [Fact]
    public void Read_rejects_a_served_duration_whose_seconds_are_not_an_integer()
    {
        var act = () => JsonSerializer.Deserialize<DeduplicationPeriod>(
            "{\"DeduplicationDuration\":{\"value\":{\"seconds\":\"half\",\"nanos\":0}}}",
            RestRefitSettings.SerializerOptions);

        act.Should().Throw<JsonException>().WithMessage("*half*");
    }

    [Fact]
    public void Read_rejects_a_period_that_sets_both_arms()
    {
        var act = () => JsonSerializer.Deserialize<DeduplicationPeriod>(
            "{\"DeduplicationOffset\":{\"value\":42},\"DeduplicationDuration\":\"30s\"}",
            RestRefitSettings.SerializerOptions);

        act.Should().Throw<JsonException>()
            .WithMessage("*DeduplicationOffset*")
            .WithMessage("*DeduplicationDuration*");
    }

    [Fact]
    public void Write_rejects_a_period_that_sets_both_arms()
    {
        var period = new DeduplicationPeriod { DeduplicationOffset = "42", DeduplicationDuration = "30s" };

        var act = () => JsonSerializer.Serialize(period, RestRefitSettings.SerializerOptions);

        act.Should().Throw<JsonException>()
            .WithMessage("*DeduplicationOffset*")
            .WithMessage("*DeduplicationDuration*");
    }

    [Fact]
    public void Read_keeps_an_arm_our_specification_does_not_declare()
    {
        var period = JsonSerializer.Deserialize<DeduplicationPeriod>(
            "{\"Empty\":{}}", RestRefitSettings.SerializerOptions);

        period.Should().NotBeNull();
        period!.AdditionalProperties.Should().ContainKey("Empty");
    }

    [Fact]
    public void Read_returns_null_for_an_absent_period()
    {
        var period = JsonSerializer.Deserialize<DeduplicationPeriod>(
            "null", RestRefitSettings.SerializerOptions);

        period.Should().BeNull();
    }
}
