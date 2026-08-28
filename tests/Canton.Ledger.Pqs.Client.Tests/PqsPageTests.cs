// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class PqsPageTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_throws_when_limit_is_not_positive(int invalidLimit)
    {
        var act = () => new PqsPage(invalidLimit);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("limit");
    }

    [Fact]
    public void Constructor_throws_when_offset_is_negative()
    {
        var act = () => new PqsPage(limit: 10, offset: -1);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("offset");
    }

    [Fact]
    public void Constructor_defaults_offset_to_zero()
    {
        var page = new PqsPage(limit: 25);

        page.Limit.Should().Be(25);
        page.Offset.Should().Be(0);
    }

    [Fact]
    public void PqsPage_instances_with_equal_values_are_equal()
    {
        new PqsPage(10, 5).Should().Be(new PqsPage(10, 5));
        new PqsPage(10, 5).Should().NotBe(new PqsPage(10, 6));
    }
}
