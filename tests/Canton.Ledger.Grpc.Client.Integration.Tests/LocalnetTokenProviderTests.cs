// Copyright © 2026 Peaceful Studio OÜ. All rights reserved.

using Xunit;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

public class LocalnetTokenProviderTests
{
    [Fact]
    public async Task GetTokenAsync_returns_the_wrapped_provider_token()
    {
        var provider = new LocalnetTokenProvider(_ => new ValueTask<string>("tok-123"));

        var token = await provider.GetTokenAsync(TestContext.Current.CancellationToken);

        Assert.Equal("tok-123", token);
    }
}
