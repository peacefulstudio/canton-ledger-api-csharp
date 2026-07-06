// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Authentication;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

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
        var token = await ITokenProvider.None.GetTokenAsync(TestContext.Current.CancellationToken);

        token.Should().BeEmpty();
    }
}
