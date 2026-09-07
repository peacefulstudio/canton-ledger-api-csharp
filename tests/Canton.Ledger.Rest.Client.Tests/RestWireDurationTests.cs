// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestWireDurationTests
{
    [Theory]
    [InlineData(0, "0s")]
    [InlineData(500, "0.5s")]
    [InlineData(1_500, "1.5s")]
    [InlineData(5_000, "5s")]
    public void ToWireDuration_renders_the_proto3_canonical_form(int milliseconds, string expected)
    {
        RestWireConversions.ToWireDuration(TimeSpan.FromMilliseconds(milliseconds)).Should().Be(expected);
    }

    [Fact]
    public void ToWireDuration_renders_a_sub_millisecond_bound_to_nine_digit_precision()
    {
        RestWireConversions.ToWireDuration(TimeSpan.FromTicks(1)).Should().Be("0.0000001s");
    }

    [Fact]
    public void ToWireDuration_renders_a_bound_that_reads_back_as_the_parts_proto3_requires()
    {
        var rendered = RestWireConversions.ToWireDuration(TimeSpan.FromMilliseconds(1_500));

        WireDuration.PartsOf(rendered).Should().Be((1L, 500_000_000),
            "the participant reads the whole seconds and the fraction together");
    }

    [Fact]
    public void ToWireDuration_refuses_a_strictly_negative_bound()
    {
        var act = () => RestWireConversions.ToWireDuration(TimeSpan.FromMilliseconds(-1));

        act.Should().Throw<ArgumentOutOfRangeException>(
            "a delay that has already elapsed has no wire representation");
    }

    [Fact]
    public void ToWireDuration_accepts_a_zero_bound()
    {
        var act = () => RestWireConversions.ToWireDuration(TimeSpan.Zero);

        act.Should().NotThrow().Which.Should().Be("0s");
    }
}
