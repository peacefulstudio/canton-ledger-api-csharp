// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Kernel.Telemetry;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestActivityHandlerTests : IDisposable
{
    private readonly List<Activity> _activities = [];
    private readonly ActivityListener _listener;
    private readonly RecordingHttpHandler _transport = new();
    private readonly string _ownHost = $"{Guid.NewGuid():N}.ledger.example";
    private readonly HttpClient _client;

    public RestActivityHandlerTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == LedgerActivitySource.NameFor<RestLedgerClient>(),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (CapturesOwnRequest(activity)) _activities.Add(activity);
            },
        };
        ActivitySource.AddActivityListener(_listener);

        var handler = new RestActivityHandler { InnerHandler = _transport };
        _client = new HttpClient(handler) { BaseAddress = new Uri($"http://{_ownHost}:7575") };
    }

    private bool CapturesOwnRequest(Activity activity) =>
        activity.GetTagItem(RestActivityHandler.UrlFull) is string url
        && url.StartsWith($"http://{_ownHost}:7575", StringComparison.Ordinal);

    public void Dispose()
    {
        _listener.Dispose();
        _client.Dispose();
    }

    [Fact]
    public async Task SendAsync_emits_a_client_span_with_HTTP_semantic_convention_tags()
    {
        await _client.GetAsync(new Uri("/v2/version", UriKind.Relative), TestContext.Current.CancellationToken);

        var activity = _activities.Should().ContainSingle().Subject;
        activity.Kind.Should().Be(ActivityKind.Client);
        activity.DisplayName.Should().Be("GET");
        activity.GetTagItem("http.request.method").Should().Be("GET");
        activity.GetTagItem("server.address").Should().Be(_ownHost);
        activity.GetTagItem("server.port").Should().Be(7575);
        activity.GetTagItem("url.full").Should().Be($"http://{_ownHost}:7575/v2/version");
        activity.GetTagItem("http.response.status_code").Should().Be(200);
        activity.Status.Should().Be(ActivityStatusCode.Unset);
    }

    [Fact]
    public async Task SendAsync_marks_the_span_as_error_on_a_non_success_status_code()
    {
        _transport.WithResponse(HttpStatusCode.ServiceUnavailable, "not ready");

        await _client.GetAsync(new Uri("/readyz", UriKind.Relative), TestContext.Current.CancellationToken);

        var activity = _activities.Should().ContainSingle().Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem("http.response.status_code").Should().Be(503);
        activity.GetTagItem("error.type").Should().Be("503");
    }

    [Fact]
    public async Task SendAsync_records_error_type_and_rethrows_when_the_transport_throws()
    {
        var transportFailure = new HttpRequestException("Connection refused");
        _transport.WithTransportException(transportFailure);

        var act = () => _client.GetAsync(new Uri("/v2/version", UriKind.Relative), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.Should().BeSameAs(transportFailure);

        var activity = _activities.Should().ContainSingle().Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem("error.type").Should().Be(typeof(HttpRequestException).FullName);
        activity.GetTagItem("http.response.status_code").Should().BeNull();
    }
}
