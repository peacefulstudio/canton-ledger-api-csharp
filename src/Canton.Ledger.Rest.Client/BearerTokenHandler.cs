// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http.Headers;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Sets the <c>Authorization: Bearer</c> header on every outgoing request from the
/// <see cref="ITokenProvider"/> registered in the container, mirroring the gRPC client's
/// auth-header semantics: <see cref="ITokenProvider.None"/> skips the header entirely,
/// and an empty token fails loudly instead of sending an unauthenticated request.
/// It also warns when a token would be sent over a plaintext <c>http</c> connection,
/// matching the gRPC client's insecure-transport check.
/// </summary>
/// <remarks>
/// The insecure-transport warning is emitted at most once per handler-pipeline instance.
/// Because <c>IHttpClientFactory</c> rotates handler pipelines periodically, a long-running host
/// may re-emit it after a rotation — deliberately, so the misconfiguration stays visible in logs
/// without introducing the process-global mutable state that true once-per-lifetime would require.
/// </remarks>
internal sealed partial class BearerTokenHandler : DelegatingHandler
{
    private const string BearerScheme = "Bearer";

    private readonly ITokenProvider _tokenProvider;
    private readonly ILogger<BearerTokenHandler> _logger;
    private int _plaintextTransportWarned;

    /// <summary>
    /// Creates the handler around the token provider whose tokens authenticate each request.
    /// </summary>
    /// <param name="tokenProvider">The provider of bearer tokens; <see cref="ITokenProvider.None"/> disables the header.</param>
    /// <param name="logger">Logger for the insecure-transport warning; defaults to a no-op logger.</param>
    public BearerTokenHandler(ITokenProvider tokenProvider, ILogger<BearerTokenHandler>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        _tokenProvider = tokenProvider;
        _logger = logger ?? NullLogger<BearerTokenHandler>.Instance;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.ResolveBearerTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token is not null)
        {
            WarnOnceIfPlaintextTransport(request.RequestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, token);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private void WarnOnceIfPlaintextTransport(Uri? requestUri)
    {
        if (requestUri is null || requestUri.Scheme != Uri.UriSchemeHttp)
            return;

        if (Interlocked.Exchange(ref _plaintextTransportWarned, 1) == 0)
            LogInsecureCredentialTransport(_logger, requestUri.GetLeftPart(UriPartial.Authority));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "REST client will send bearer tokens over plaintext http to {Endpoint}, where anyone on the network path can read and replay them. Use an https HttpAddress for anything beyond local development.")]
    private static partial void LogInsecureCredentialTransport(ILogger logger, string endpoint);
}
