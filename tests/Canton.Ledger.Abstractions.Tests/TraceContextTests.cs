// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Abstractions.Tests;

public class TraceContextTests
{
    private const string ValidTraceparent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    [Fact]
    public void Constructor_rejects_a_null_traceparent()
    {
        var construct = () => new TraceContext(null!, "peaceful=1");

        construct.Should().Throw<ArgumentNullException>().WithParameterName("Traceparent");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n")]
    public void Constructor_rejects_an_empty_or_whitespace_traceparent(string traceparent)
    {
        var construct = () => new TraceContext(traceparent, null);

        construct.Should().Throw<ArgumentException>().WithParameterName("Traceparent");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void With_rejects_an_empty_or_whitespace_traceparent(string traceparent)
    {
        var original = new TraceContext(ValidTraceparent, "peaceful=1");

        var copy = () => original with { Traceparent = traceparent };

        copy.Should().Throw<ArgumentException>().WithParameterName("Traceparent");
    }

    [Fact]
    public void With_rejects_a_null_traceparent()
    {
        var original = new TraceContext(ValidTraceparent, null);

        var copy = () => original with { Traceparent = null! };

        copy.Should().Throw<ArgumentNullException>().WithParameterName("Traceparent");
    }

    [Fact]
    public void A_valid_trace_context_keeps_deconstruction_with_and_value_equality()
    {
        var context = new TraceContext(ValidTraceparent, "peaceful=1");

        var (traceparent, tracestate) = context;
        var withoutTracestate = context with { Tracestate = null };

        traceparent.Should().Be("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");
        tracestate.Should().Be("peaceful=1");
        withoutTracestate.Tracestate.Should().BeNull();
        withoutTracestate.Traceparent.Should().Be("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");
        context.Should().Be(new TraceContext("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01", "peaceful=1"));
        context.Should().NotBe(withoutTracestate);
    }
}
