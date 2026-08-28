// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class FakeTokenProviderTests
{
    [Fact]
    public async Task WithToken_GetTokenAsync_returns_the_staged_token()
    {
        var provider = FakeTokenProvider.WithToken("staged-token");

        var token = await provider.GetTokenAsync(TestContext.Current.CancellationToken);

        token.Should().Be("staged-token");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithToken_throws_for_null_or_whitespace_token(string? token)
    {
        var act = () => FakeTokenProvider.WithToken(token!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task WithFailure_GetTokenAsync_faults_with_the_staged_exception()
    {
        var exception = new InvalidOperationException("identity provider unreachable");
        var provider = FakeTokenProvider.WithFailure(exception);

        var act = () => provider.GetTokenAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(exception);
    }

    [Fact]
    public void WithFailure_throws_for_null_exception()
    {
        var act = () => FakeTokenProvider.WithFailure(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
