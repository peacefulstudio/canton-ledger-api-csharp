// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using System.Net;
using Canton.Ledger.Auth.TokenGeneration;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Canton.Ledger.Auth.Tests;

public class ClientCredentialsProviderTests
{
    private static ClientCredentialsOptions CreateOptions(string? audience = null) => new()
    {
        ClientId = "test-client",
        ClientSecret = "test-secret",
        Domain = "https://auth.example.com",
        Audience = audience
    };

    private static ClientCredentialsProvider CreateProvider(
        ClientCredentialsOptions options,
        FakeHttpHandler handler,
        TimeProvider? timeProvider = null)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("CantonAuth").Returns(_ => new HttpClient(handler));
        return new ClientCredentialsProvider(
            Options.Create(options),
            factory,
            timeProvider ?? TimeProvider.System);
    }

    [Fact]
    public async Task GetTokenAsync_sends_correct_form_data_to_TokenEndpoint()
    {
        var handler = new FakeHttpHandler();
        var options = CreateOptions();
        using var provider = CreateProvider(options, handler);

        await provider.GetTokenAsync();

        handler.LastRequest!.RequestUri.Should().Be(new Uri("https://auth.example.com/oauth/token"));
        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
        handler.LastRequestBody.Should().Contain("grant_type=client_credentials");
        handler.LastRequestBody.Should().Contain("client_id=test-client");
        handler.LastRequestBody.Should().Contain("client_secret=test-secret");
    }

    [Fact]
    public async Task GetTokenAsync_includes_Audience_when_configured()
    {
        var handler = new FakeHttpHandler();
        var options = CreateOptions(audience: "https://canton.network/");
        using var provider = CreateProvider(options, handler);

        await provider.GetTokenAsync();

        handler.LastRequestBody.Should().Contain("audience=https%3A%2F%2Fcanton.network%2F");
    }

    [Fact]
    public async Task GetTokenAsync_returns_access_token_from_response()
    {
        var handler = new FakeHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """{"access_token":"my-real-token","expires_in":3600,"token_type":"Bearer"}""");
        using var provider = CreateProvider(CreateOptions(), handler);

        var token = await provider.GetTokenAsync();

        token.Should().Be("my-real-token");
    }

    [Fact]
    public async Task GetTokenAsync_throws_on_http_error()
    {
        var handler = new FakeHttpHandler().WithResponse(
            HttpStatusCode.Unauthorized,
            """{"error":"invalid_client"}""");
        using var provider = CreateProvider(CreateOptions(), handler);

        var act = () => provider.GetTokenAsync();

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetTokenAsync_throws_on_malformed_response()
    {
        var handler = new FakeHttpHandler().WithResponse(
            HttpStatusCode.OK,
            "not-json");
        using var provider = CreateProvider(CreateOptions(), handler);

        var act = () => provider.GetTokenAsync();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task GetTokenAsync_returns_cached_token_when_not_expired()
    {
        var handler = new FakeHttpHandler();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var provider = CreateProvider(CreateOptions(), handler, timeProvider);

        var token1 = await provider.GetTokenAsync();
        var token2 = await provider.GetTokenAsync();

        token1.Should().Be(token2);
        handler.CallCount.Should().Be(1, "second call should use cache");
    }

    [Fact]
    public async Task GetTokenAsync_refreshes_token_when_within_SafetyMargin()
    {
        var handler = new FakeHttpHandler();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var options = CreateOptions();
        options.SafetyMargin = TimeSpan.FromSeconds(30);
        using var provider = CreateProvider(options, handler, timeProvider);

        await provider.GetTokenAsync();
        handler.CallCount.Should().Be(1);

        // Advance time to within safety margin (3600 - 30 = 3570 seconds)
        timeProvider.Advance(TimeSpan.FromSeconds(3571));

        await provider.GetTokenAsync();
        handler.CallCount.Should().Be(2, "token should be refreshed when within safety margin");
    }

    [Fact]
    public async Task GetTokenAsync_concurrent_callers_share_single_refresh()
    {
        var handler = new FakeHttpHandler();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var provider = CreateProvider(CreateOptions(), handler, timeProvider);

        // Populate cache with initial token
        await provider.GetTokenAsync();
        handler.CallCount.Should().Be(1);

        // Expire the cached token so all concurrent tasks trigger a refresh
        timeProvider.Advance(TimeSpan.FromHours(2));

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => provider.GetTokenAsync())
            .ToArray();

        var tokens = await Task.WhenAll(tasks);

        tokens.Should().AllBe("fake-token");
        handler.CallCount.Should().Be(2, "concurrent callers should share a single HTTP request after cache expiry");
    }

    [Fact]
    public async Task GetTokenAsync_refreshed_token_is_never_paired_with_stale_value()
    {
        // Verifies that after a token refresh, callers always see the new token —
        // never the old token extended by the new expiry. This would happen if
        // the write order in RequestTokenAsync published expiry before token.
        var handler = new FakeHttpHandler().WithResponseSequence(
            """{"access_token":"token-v1","expires_in":3600,"token_type":"Bearer"}""",
            """{"access_token":"token-v2","expires_in":3600,"token_type":"Bearer"}""");
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var provider = CreateProvider(CreateOptions(), handler, timeProvider);

        var first = await provider.GetTokenAsync();
        first.Should().Be("token-v1");

        // Expire the cache
        timeProvider.Advance(TimeSpan.FromHours(2));

        // Fire concurrent reads — all should see token-v2, never token-v1
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => provider.GetTokenAsync())
            .ToArray();

        var tokens = await Task.WhenAll(tasks);

        tokens.Should().AllBe("token-v2", "no caller should see the old token after refresh");
    }

    [Fact]
    public async Task GetTokenAsync_throws_on_zero_expires_in()
    {
        var handler = new FakeHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """{"access_token":"token","expires_in":0,"token_type":"Bearer"}""");
        using var provider = CreateProvider(CreateOptions(), handler);

        var act = () => provider.GetTokenAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*invalid expires_in*");
    }

    [Fact]
    public async Task GetTokenAsync_throws_on_negative_expires_in()
    {
        var handler = new FakeHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """{"access_token":"token","expires_in":-1,"token_type":"Bearer"}""");
        using var provider = CreateProvider(CreateOptions(), handler);

        var act = () => provider.GetTokenAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*invalid expires_in*");
    }

    [Fact]
    public async Task GetTokenAsync_throws_on_empty_access_token_in_response()
    {
        var handler = new FakeHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """{"access_token":"","expires_in":3600,"token_type":"Bearer"}""");
        using var provider = CreateProvider(CreateOptions(), handler);

        var act = () => provider.GetTokenAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no access_token*");
    }

    [Fact]
    public async Task GetTokenAsync_throws_on_null_access_token_in_response()
    {
        var handler = new FakeHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """{"expires_in":3600,"token_type":"Bearer"}""");
        using var provider = CreateProvider(CreateOptions(), handler);

        var act = () => provider.GetTokenAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no access_token*");
    }

    [Fact]
    public async Task GetTokenAsync_propagates_cancellation()
    {
        var handler = new FakeHttpHandler();
        using var provider = CreateProvider(CreateOptions(), handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => provider.GetTokenAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
