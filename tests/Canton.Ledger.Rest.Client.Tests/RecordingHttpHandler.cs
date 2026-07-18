// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;

namespace Canton.Ledger.Rest.Client.Tests;

internal sealed class RecordingHttpHandler : HttpMessageHandler
{
    private HttpStatusCode _statusCode = HttpStatusCode.OK;
    private string _responseBody = "{}";
    private Exception? _transportException;

    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    public RecordingHttpHandler WithResponse(HttpStatusCode statusCode, string body = "{}")
    {
        _statusCode = statusCode;
        _responseBody = body;
        return this;
    }

    public RecordingHttpHandler WithTransportException(Exception transportException)
    {
        _transportException = transportException;
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        if (_transportException is not null)
            throw _transportException;

        return new HttpResponseMessage(_statusCode)
        {
            RequestMessage = request,
            Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
        };
    }
}
