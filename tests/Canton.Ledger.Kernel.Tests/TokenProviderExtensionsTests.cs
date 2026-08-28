// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public class TokenProviderExtensionsTests
{
    [Fact]
    public async Task ResolveBearerTokenAsync_returns_null_for_ITokenProvider_None()
    {
        var token = await ITokenProvider.None.ResolveBearerTokenAsync(TestContext.Current.CancellationToken);

        token.Should().BeNull();
    }

    [Fact]
    public async Task ResolveBearerTokenAsync_returns_null_for_null_provider()
    {
        ITokenProvider? provider = null;

        var token = await provider.ResolveBearerTokenAsync(TestContext.Current.CancellationToken);

        token.Should().BeNull();
    }

    [Fact]
    public async Task ResolveBearerTokenAsync_returns_the_token_from_the_provider()
    {
        var token = await new StaticTokenProvider("the-token").ResolveBearerTokenAsync(TestContext.Current.CancellationToken);

        token.Should().Be("the-token");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task ResolveBearerTokenAsync_throws_when_provider_returns_a_blank_token(string blankToken)
    {
        var act = () => new BlankTokenProvider(blankToken).ResolveBearerTokenAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"*{nameof(BlankTokenProvider)}*empty token*");
    }

    private sealed class BlankTokenProvider(string token) : ITokenProvider
    {
        public Task<string> GetTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult(token);
    }
}
