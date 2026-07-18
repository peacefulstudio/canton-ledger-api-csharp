// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Canton.Ledger.Kernel.Tests;

internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private HttpStatusCode _statusCode = HttpStatusCode.OK;
    private string _responseBody = """{"access_token":"fake-token","expires_in":3600,"token_type":"Bearer"}""";
    private readonly ConcurrentQueue<string> _responseSequence = new();
    private TimeSpan? _delay;

    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }
    private int _callCount;
    public int CallCount => _callCount;

    public FakeHttpHandler WithResponse(HttpStatusCode statusCode, string body)
    {
        _statusCode = statusCode;
        _responseBody = body;
        return this;
    }

    public FakeHttpHandler WithResponseSequence(params string[] responses)
    {
        foreach (var r in responses)
            _responseSequence.Enqueue(r);
        return this;
    }

    public FakeHttpHandler WithDelay(TimeSpan delay)
    {
        _delay = delay;
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        Interlocked.Increment(ref _callCount);

        if (_delay is { } delay)
            await Task.Delay(delay, cancellationToken);

        var body = _responseSequence.TryDequeue(out var next) ? next : _responseBody;

        return new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
