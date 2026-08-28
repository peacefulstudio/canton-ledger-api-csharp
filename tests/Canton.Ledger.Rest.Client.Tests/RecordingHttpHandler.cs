// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Canton.Ledger.Rest.Client.Tests;

internal sealed class RecordingHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode StatusCode, string Body)> _responsesByPath = [];
    private readonly List<(string Name, string Value)> _responseHeaders = [];
    private HttpStatusCode _statusCode = HttpStatusCode.OK;
    private string _responseBody = "{}";
    private byte[]? _responseBytes;
    private string _responseMediaType = "application/octet-stream";
    private Exception? _transportException;

    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }
    public byte[]? LastRequestBytes { get; private set; }

    public RecordingHttpHandler WithResponse(HttpStatusCode statusCode, string body = "{}")
    {
        _statusCode = statusCode;
        _responseBody = body;
        _responseBytes = null;
        return this;
    }

    public RecordingHttpHandler WithResponseForPath(string pathAndQuery, HttpStatusCode statusCode, string body)
    {
        _responsesByPath[pathAndQuery] = (statusCode, body);
        return this;
    }

    public RecordingHttpHandler WithBinaryResponse(HttpStatusCode statusCode, byte[] body, string mediaType)
    {
        _statusCode = statusCode;
        _responseBytes = body;
        _responseMediaType = mediaType;
        return this;
    }

    public RecordingHttpHandler WithResponseHeader(string name, string value)
    {
        _responseHeaders.Add((name, value));
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
        {
            LastRequestBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            LastRequestBody = Encoding.UTF8.GetString(LastRequestBytes);
        }

        if (_transportException is not null)
            throw _transportException;

        var response = BuildResponse(request);
        response.RequestMessage = request;
        foreach (var (name, value) in _responseHeaders)
            response.Headers.TryAddWithoutValidation(name, value);

        return response;
    }

    private HttpResponseMessage BuildResponse(HttpRequestMessage request)
    {
        if (request.RequestUri is { } uri && _responsesByPath.TryGetValue(uri.PathAndQuery, out var forPath))
            return JsonResponse(forPath.StatusCode, forPath.Body);

        if (_responseBytes is null)
            return JsonResponse(_statusCode, _responseBody);

        return new HttpResponseMessage(_statusCode)
        {
            Content = new ByteArrayContent(_responseBytes)
            {
                Headers = { ContentType = new MediaTypeHeaderValue(_responseMediaType) }
            }
        };
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
