// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Canton.Ledger.Rest.Client.Tests;

internal sealed class RecordingHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode StatusCode, string Body)> _responsesByPath = [];
    private readonly List<(HttpStatusCode StatusCode, string Body)> _responseSequence = [];
    private readonly List<(string PathAndQuery, string? Body)> _requests = [];
    private readonly List<(string Name, string Value)> _responseHeaders = [];
    private int _nextSequencedResponse;
    private HttpStatusCode _statusCode = HttpStatusCode.OK;
    private string _responseBody = "{}";
    private byte[]? _responseBytes;
    private string _responseMediaType = "application/octet-stream";
    private Exception? _transportException;
    private bool _hangsUntilCancelled;
    private TimeSpan _responseDelay = TimeSpan.Zero;

    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }
    public byte[]? LastRequestBytes { get; private set; }
    public IReadOnlyList<(string PathAndQuery, string? Body)> Requests => _requests;

    public RecordingHttpHandler WithResponse(HttpStatusCode statusCode, string body = "{}")
    {
        _statusCode = statusCode;
        _responseBody = body;
        _responseBytes = null;
        return this;
    }

    public RecordingHttpHandler WithResponseSequence(params (HttpStatusCode StatusCode, string Body)[] responses)
    {
        _responseSequence.AddRange(responses);
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

    public RecordingHttpHandler WithResponseDelay(TimeSpan delay)
    {
        _responseDelay = delay;
        return this;
    }

    public RecordingHttpHandler WithNoAnswerUntilCancelled()
    {
        _hangsUntilCancelled = true;
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastRequestBody = null;
        LastRequestBytes = null;
        if (request.Content is not null)
        {
            LastRequestBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            LastRequestBody = Encoding.UTF8.GetString(LastRequestBytes);
        }

        _requests.Add((request.RequestUri?.PathAndQuery ?? string.Empty, LastRequestBody));

        if (_hangsUntilCancelled)
            await Task.Delay(Timeout.Infinite, cancellationToken);

        if (_responseDelay > TimeSpan.Zero)
            await Task.Delay(_responseDelay, cancellationToken);

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
        if (RegisteredResponseFor(request.RequestUri) is { } forPath)
            return JsonResponse(forPath.StatusCode, forPath.Body);

        if (_responseSequence.Count > 0)
        {
            var sequenced = _responseSequence[Math.Min(_nextSequencedResponse, _responseSequence.Count - 1)];
            _nextSequencedResponse++;
            return JsonResponse(sequenced.StatusCode, sequenced.Body);
        }

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

    private (HttpStatusCode StatusCode, string Body)? RegisteredResponseFor(Uri? uri)
    {
        if (uri is null)
            return null;

        if (_responsesByPath.TryGetValue(uri.PathAndQuery, out var exact))
            return exact;

        return _responsesByPath.TryGetValue(uri.AbsolutePath, out var byPath) ? byPath : null;
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
