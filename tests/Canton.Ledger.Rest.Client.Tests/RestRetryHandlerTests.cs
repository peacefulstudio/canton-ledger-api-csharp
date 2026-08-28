// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Net;
using System.Text;
using AwesomeAssertions;
using Canton.Ledger.Kernel.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestRetryHandlerTests
{
    private const string SubmitPath = "/v2/commands/submit-and-wait";

    private sealed class ForwardOnlyStream(byte[] payload) : Stream
    {
        private readonly MemoryStream _inner = new(payload);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CountingHandler(params Exception?[] outcomePerAttempt) : HttpMessageHandler
    {
        public int Attempts { get; private set; }
        public List<string> ObservedBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                using var body = new MemoryStream();
                await request.Content.CopyToAsync(body, cancellationToken);
                ObservedBodies.Add(Encoding.UTF8.GetString(body.ToArray()));
            }

            var outcome = Attempts < outcomePerAttempt.Length ? outcomePerAttempt[Attempts] : null;
            Attempts++;
            if (outcome is not null)
                throw outcome;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"offset":0}""", Encoding.UTF8, "application/json")
            };
        }
    }

    private static HttpClient ClientOver(CountingHandler transport, RetryOptions retry) =>
        new(new RestRetryHandler(
            Options.Create(new RestLedgerClientOptions { HttpAddress = "http://localhost:7575", Retry = retry }))
        {
            InnerHandler = transport
        })
        {
            BaseAddress = new Uri("http://localhost:7575")
        };

    private static RetryOptions Fast(int maxRetryAttempts) =>
        new() { Enabled = true, MaxRetryAttempts = maxRetryAttempts, Delay = TimeSpan.Zero };

    [Fact]
    public async Task SendAsync_does_not_retry_when_Retry_is_disabled()
    {
        var transport = new CountingHandler(new HttpRequestException("connection refused"));
        using var client = ClientOver(transport, new RetryOptions());

        var act = async () => await client.GetAsync("/v2/state/ledger-end", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>();
        transport.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_retries_a_transient_connection_failure_until_it_succeeds()
    {
        var transport = new CountingHandler(new HttpRequestException("connection refused"), new HttpRequestException("connection refused"));
        using var client = ClientOver(transport, Fast(3));

        var response = await client.GetAsync("/v2/state/ledger-end", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        transport.Attempts.Should().Be(3);
    }

    [Fact]
    public async Task SendAsync_stops_after_MaxRetryAttempts_and_surfaces_the_last_failure()
    {
        var transport = new CountingHandler(
            new HttpRequestException("1"), new HttpRequestException("2"), new HttpRequestException("3"), new HttpRequestException("4"));
        using var client = ClientOver(transport, Fast(2));

        var act = async () => await client.GetAsync("/v2/state/ledger-end", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>();
        transport.Attempts.Should().Be(3);
    }

    [Fact]
    public async Task SendAsync_retries_a_client_side_timeout()
    {
        var transport = new CountingHandler(new TaskCanceledException("timed out", new TimeoutException()));
        using var client = ClientOver(transport, Fast(1));

        var response = await client.GetAsync("/v2/state/ledger-end", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        transport.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_does_not_retry_a_non_transient_failure()
    {
        var transport = new CountingHandler(new InvalidOperationException("malformed request"));
        using var client = ClientOver(transport, Fast(3));

        var act = async () => await client.GetAsync("/v2/state/ledger-end", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        transport.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_does_not_retry_a_caller_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var transport = new CountingHandler(new TaskCanceledException("cancelled"));
        using var client = ClientOver(transport, Fast(3));

        var act = async () => await client.GetAsync("/v2/state/ledger-end", cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        transport.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task SendAsync_replays_the_request_body_on_every_retry()
    {
        var transport = new CountingHandler(new HttpRequestException("connection refused"));
        using var client = ClientOver(transport, Fast(1));

        using var request = new HttpRequestMessage(HttpMethod.Post, SubmitPath)
        {
            Content = new StreamContent(new ForwardOnlyStream(Encoding.UTF8.GetBytes("""{"commandId":"stable"}""")))
        };
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        transport.ObservedBodies.Should().HaveCount(2).And.AllBe("""{"commandId":"stable"}""");
    }

    [Fact]
    public async Task SendAsync_records_a_RetryAttempt_activity_on_the_RestLedgerClient_source()
    {
        using var capture = ActivityCapture.Of(RestLedgerClient.ActivitySourceName);

        var transport = new CountingHandler(new HttpRequestException("connection refused"));
        using var client = ClientOver(transport, Fast(1));

        await client.GetAsync("/v2/state/ledger-end", TestContext.Current.CancellationToken);

        var retryActivity = capture.Activities.Should().ContainSingle(a => a.OperationName == "RestLedgerClient.RetryAttempt").Subject;
        retryActivity.GetTagItem("retry.attempt").Should().Be(0);
        retryActivity.Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task AddRestLedgerClient_puts_the_retry_handler_in_the_request_pipeline()
    {
        var transport = new CountingHandler(new HttpRequestException("connection refused"));
        var services = new ServiceCollection();
        services.AddRestLedgerClient(options =>
        {
            options.HttpAddress = "http://localhost:7575";
            options.Retry = Fast(1);
        });
        services.AddHttpClient(ServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => transport);
        using var provider = services.BuildServiceProvider();

        var offset = await provider.GetRequiredService<RestLedgerClient>()
            .GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        offset.Value.Should().Be(0);
        transport.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task AddRestLedgerClient_leaves_the_pipeline_single_shot_when_Retry_is_not_configured()
    {
        var transport = new CountingHandler(new HttpRequestException("connection refused"));
        var services = new ServiceCollection();
        services.AddRestLedgerClient(options => options.HttpAddress = "http://localhost:7575");
        services.AddHttpClient(ServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => transport);
        using var provider = services.BuildServiceProvider();

        var act = async () => await provider.GetRequiredService<RestLedgerClient>()
            .GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>();
        transport.Attempts.Should().Be(1);
    }
}
