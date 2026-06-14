// Copyright © 2026 Peaceful Studio OÜ. All rights reserved.

using FluentAssertions;
using Xunit;

namespace Canton.Ledger.Auth.Tests;

public class StaticTokenProviderTests
{
    [Fact]
    public async Task GetTokenAsync_returns_configured_token()
    {
        var provider = new StaticTokenProvider("my-test-token");

        var token = await provider.GetTokenAsync(TestContext.Current.CancellationToken);

        token.Should().Be("my-test-token");
    }

    [Fact]
    public async Task GetTokenAsync_returns_same_token_on_multiple_calls()
    {
        var provider = new StaticTokenProvider("stable-token");

        var token1 = await provider.GetTokenAsync(TestContext.Current.CancellationToken);
        var token2 = await provider.GetTokenAsync(TestContext.Current.CancellationToken);

        token1.Should().Be("stable-token");
        token2.Should().Be("stable-token");
    }

    [Fact]
    public void Constructor_throws_on_null_token()
    {
        var act = () => new StaticTokenProvider(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_throws_on_empty_token()
    {
        var act = () => new StaticTokenProvider("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_throws_on_whitespace_token()
    {
        var act = () => new StaticTokenProvider("   ");

        act.Should().Throw<ArgumentException>();
    }
}
