// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestStreamBodyReaderTests
{
    private sealed record Sample(int Value);

    [Fact]
    public void Parse_decodes_one_entry_per_array_element()
    {
        var body = "[{\"Value\":1},{\"Value\":2},{\"Value\":3}]";

        var entries = RestStreamBodyReader.Parse<Sample>(body);

        entries.Should().Equal(new Sample(1), new Sample(2), new Sample(3));
    }

    [Fact]
    public void Parse_returns_an_empty_list_for_an_empty_array()
    {
        RestStreamBodyReader.Parse<Sample>("[]").Should().BeEmpty();
    }

    [Fact]
    public void Parse_throws_JsonException_when_an_array_element_is_null()
    {
        var act = () => RestStreamBodyReader.Parse<Sample>("[null]");

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Parse_throws_JsonException_for_a_newline_delimited_body()
    {
        var act = () => RestStreamBodyReader.Parse<Sample>("{\"Value\":1}\n{\"Value\":2}");

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Parse_throws_ArgumentNullException_when_body_is_null()
    {
        var act = () => RestStreamBodyReader.Parse<Sample>(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
