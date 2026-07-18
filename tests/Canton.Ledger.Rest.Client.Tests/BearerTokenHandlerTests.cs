// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Authentication;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class BearerTokenHandlerTests
{
    private const string InsecureTransportMarker = "plaintext http";

    private static (HttpClient Client, RecordingHttpHandler Transport) BuildClient(
        ITokenProvider tokenProvider,
        string baseAddress = "http://localhost:7575",
        ILogger<BearerTokenHandler>? logger = null)
    {
        var transport = new RecordingHttpHandler();
        var handler = new BearerTokenHandler(tokenProvider, logger) { InnerHandler = transport };
        return (new HttpClient(handler) { BaseAddress = new Uri(baseAddress) }, transport);
    }

    [Fact]
    public async Task SendAsync_sets_bearer_Authorization_header_from_token_provider()
    {
        var (client, transport) = BuildClient(new StaticTokenProvider("the-token"));

        await client.GetAsync(new Uri("/v2/version", UriKind.Relative), TestContext.Current.CancellationToken);

        var authorization = transport.LastRequest!.Headers.Authorization;
        authorization.Should().NotBeNull();
        authorization.Scheme.Should().Be("Bearer");
        authorization.Parameter.Should().Be("the-token");
    }

    [Fact]
    public async Task SendAsync_skips_Authorization_header_for_ITokenProvider_None()
    {
        var (client, transport) = BuildClient(ITokenProvider.None);

        await client.GetAsync(new Uri("/v2/version", UriKind.Relative), TestContext.Current.CancellationToken);

        transport.LastRequest!.Headers.Authorization.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task SendAsync_throws_when_token_provider_returns_a_blank_token(string blankToken)
    {
        var (client, _) = BuildClient(new BlankTokenProvider(blankToken));

        var act = () => client.GetAsync(new Uri("/v2/version", UriKind.Relative), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"*{nameof(BlankTokenProvider)}*empty token*");
    }

    [Fact]
    public async Task SendAsync_warns_when_bearer_tokens_would_be_sent_over_plaintext_http()
    {
        var loggerFactory = new CapturingLoggerFactory();
        var (client, _) = BuildClient(
            new StaticTokenProvider("the-token"),
            "http://participant.internal:7575",
            new Logger<BearerTokenHandler>(loggerFactory));

        await client.GetAsync(new Uri("/v2/version", UriKind.Relative), TestContext.Current.CancellationToken);

        loggerFactory.Records.Should().Contain(r =>
            r.Level == LogLevel.Warning
            && r.Message.Contains(InsecureTransportMarker)
            && r.Message.Contains("http://participant.internal:7575"));
    }

    [Fact]
    public async Task SendAsync_warns_at_most_once_across_requests_over_plaintext_http()
    {
        var loggerFactory = new CapturingLoggerFactory();
        var (client, _) = BuildClient(
            new StaticTokenProvider("the-token"),
            "http://participant.internal:7575",
            new Logger<BearerTokenHandler>(loggerFactory));

        await client.GetAsync(new Uri("/v2/version", UriKind.Relative), TestContext.Current.CancellationToken);
        await client.GetAsync(new Uri("/v2/version", UriKind.Relative), TestContext.Current.CancellationToken);

        loggerFactory.Records.Count(r => r.Message.Contains(InsecureTransportMarker)).Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_does_not_warn_about_plaintext_transport_over_https()
    {
        var loggerFactory = new CapturingLoggerFactory();
        var (client, _) = BuildClient(
            new StaticTokenProvider("the-token"),
            "https://participant.internal:7575",
            new Logger<BearerTokenHandler>(loggerFactory));

        await client.GetAsync(new Uri("/v2/version", UriKind.Relative), TestContext.Current.CancellationToken);

        loggerFactory.Records.Should().NotContain(r => r.Message.Contains(InsecureTransportMarker));
    }

    [Fact]
    public async Task SendAsync_does_not_warn_about_plaintext_transport_for_ITokenProvider_None()
    {
        var loggerFactory = new CapturingLoggerFactory();
        var (client, _) = BuildClient(
            ITokenProvider.None,
            "http://participant.internal:7575",
            new Logger<BearerTokenHandler>(loggerFactory));

        await client.GetAsync(new Uri("/v2/version", UriKind.Relative), TestContext.Current.CancellationToken);

        loggerFactory.Records.Should().NotContain(r => r.Message.Contains(InsecureTransportMarker));
    }

    [Fact]
    public async Task SendAsync_does_not_warn_when_the_request_has_no_uri()
    {
        var loggerFactory = new CapturingLoggerFactory();
        var transport = new RecordingHttpHandler();
        var handler = new BearerTokenHandler(
            new StaticTokenProvider("the-token"),
            new Logger<BearerTokenHandler>(loggerFactory))
        { InnerHandler = transport };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, (Uri?)null);

        await invoker.SendAsync(request, TestContext.Current.CancellationToken);

        transport.LastRequest!.Headers.Authorization!.Parameter.Should().Be("the-token");
        loggerFactory.Records.Should().NotContain(r => r.Message.Contains(InsecureTransportMarker));
    }

    private sealed class BlankTokenProvider(string token) : ITokenProvider
    {
        public Task<string> GetTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult(token);
    }
}
