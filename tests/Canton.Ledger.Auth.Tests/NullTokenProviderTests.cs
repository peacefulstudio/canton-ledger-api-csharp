// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using FluentAssertions;
using Xunit;

namespace Canton.Ledger.Auth.Tests;

public class NullTokenProviderTests
{
    [Fact]
    public void None_returns_singleton_instance()
    {
        var a = ITokenProvider.None;
        var b = ITokenProvider.None;

        a.Should().BeSameAs(b);
    }

    [Fact]
    public void None_implements_ITokenProvider()
    {
        ITokenProvider provider = ITokenProvider.None;

        provider.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTokenAsync_returns_empty_string()
    {
        var token = await ITokenProvider.None.GetTokenAsync();

        token.Should().BeEmpty();
    }
}
