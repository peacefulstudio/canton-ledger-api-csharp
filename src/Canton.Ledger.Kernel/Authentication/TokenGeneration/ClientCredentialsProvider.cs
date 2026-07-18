// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Canton.Ledger.Kernel.Authentication.TokenGeneration;

/// <summary>
/// OAuth2 client-credentials token provider with TTL-based caching.
/// Thread-safe: concurrent callers share a single refresh request, and cached
/// tokens are reused until <c>expires_in</c> minus
/// <see cref="ClientCredentialsOptions.SafetyMargin"/>.
/// </summary>
public sealed partial class ClientCredentialsProvider : ITokenProvider, IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ClientCredentialsOptions _options;
    private readonly Uri _tokenEndpoint;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClientCredentialsProvider> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private string? _cachedToken;
    private long _expiresAtTicks;

    /// <summary>
    /// Creates a new <see cref="ClientCredentialsProvider"/>.
    /// </summary>
    /// <remarks>
    /// When registered via <c>AddCantonAuth</c>, options validation surfaces the same
    /// misconfiguration as an <see cref="OptionsValidationException"/> before this
    /// constructor runs.
    /// </remarks>
    /// <param name="options">The client-credentials configuration.</param>
    /// <param name="httpClientFactory">Factory used to create the <c>CantonAuth</c> named client.</param>
    /// <param name="timeProvider">Time source used to track token expiry.</param>
    /// <param name="logger">Optional logger; logs are discarded when omitted.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/>, <paramref name="httpClientFactory"/>, or
    /// <paramref name="timeProvider"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The options resolve to no usable token endpoint:
    /// <see cref="ClientCredentialsOptions.TokenEndpoint"/> is set but is not an absolute
    /// http/https URI, <see cref="ClientCredentialsOptions.Domain"/> ends with the
    /// <c>/oauth/token</c> path, neither is configured, or the endpoint uses plaintext
    /// <c>http</c> without <see cref="ClientCredentialsOptions.AllowInsecureTokenEndpoint"/>.
    /// </exception>
    public ClientCredentialsProvider(
        IOptions<ClientCredentialsOptions> options,
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        ILogger<ClientCredentialsProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _options = options.Value;
        _tokenEndpoint = _options.TokenGenerationEndpoint;
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
        _logger = logger ?? NullLogger<ClientCredentialsProvider>.Instance;

        if (_tokenEndpoint.Scheme == Uri.UriSchemeHttp)
            LogInsecureTokenEndpoint(_logger, _tokenEndpoint);
    }

    /// <inheritdoc />
    /// <exception cref="HttpRequestException">The token endpoint returned a non-success status code or was unreachable.</exception>
    /// <exception cref="System.Text.Json.JsonException">The token endpoint returned a body that is not valid JSON.</exception>
    /// <exception cref="InvalidOperationException">The token endpoint returned a malformed response: <see langword="null"/> after deserialization, missing <c>access_token</c>, or non-positive <c>expires_in</c>.</exception>
    /// <exception cref="TimeoutException">The token fetch exceeded <see cref="ClientCredentialsOptions.TokenAcquisitionTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled via <paramref name="cancellationToken"/>.</exception>
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        var cachedToken = Volatile.Read(ref _cachedToken);
        if (cachedToken is not null && _timeProvider.GetUtcNow().Ticks < Volatile.Read(ref _expiresAtTicks) - _options.SafetyMargin.Ticks)
            return cachedToken;

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cachedToken = Volatile.Read(ref _cachedToken);
            if (cachedToken is not null && _timeProvider.GetUtcNow().Ticks < Volatile.Read(ref _expiresAtTicks) - _options.SafetyMargin.Ticks)
                return cachedToken;

            return await RequestTokenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogTokenRefreshFailed(_logger, _tokenEndpoint, ex);
            throw;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<string> RequestTokenAsync(CancellationToken cancellationToken)
    {
        var formData = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "client_credentials"),
            new("client_id", _options.ClientId),
            new("client_secret", _options.ClientSecret)
        };

        if (_options.Audience is not null)
            formData.Add(new("audience", _options.Audience));

        var httpClient = _httpClientFactory.CreateClient("CantonAuth");
        using var content = new FormUrlEncodedContent(formData);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.TokenAcquisitionTimeout);
        var fetchToken = timeoutCts.Token;

        try
        {
            using var response = await httpClient.PostAsync(_tokenEndpoint, content, fetchToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(fetchToken).ConfigureAwait(false);
                if (errorBody.Length > 1024)
                    errorBody = string.Concat(errorBody.AsSpan(0, 1024), "… (truncated)");
                LogTokenAcquisitionFailed(_logger, _tokenEndpoint, (int)response.StatusCode, errorBody);
                response.EnsureSuccessStatusCode();
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(fetchToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Token endpoint returned null response.");

            if (string.IsNullOrEmpty(tokenResponse.AccessToken))
                throw new InvalidOperationException(
                    $"Token endpoint {_tokenEndpoint} returned a response with no access_token.");

            if (tokenResponse.ExpiresIn <= 0)
                throw new InvalidOperationException(
                    $"Token endpoint {_tokenEndpoint} returned an invalid expires_in value '{tokenResponse.ExpiresIn}'. A positive value is required.");

            Volatile.Write(ref _cachedToken, tokenResponse.AccessToken);
            Volatile.Write(ref _expiresAtTicks, (_timeProvider.GetUtcNow() + TimeSpan.FromSeconds(tokenResponse.ExpiresIn)).Ticks);

            LogTokenAcquired(_logger, _tokenEndpoint);

            return tokenResponse.AccessToken;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Token acquisition from {_tokenEndpoint} timed out after {_options.TokenAcquisitionTimeout.TotalSeconds:0.###}s.",
                ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Token acquired from {Endpoint}")]
    private static partial void LogTokenAcquired(ILogger logger, Uri endpoint);

    [LoggerMessage(Level = LogLevel.Error, Message = "Token acquisition failed from {Endpoint}: HTTP {StatusCode} — {ErrorBody}")]
    private static partial void LogTokenAcquisitionFailed(ILogger logger, Uri endpoint, int statusCode, string errorBody);

    [LoggerMessage(Level = LogLevel.Error, Message = "Token refresh failed from {Endpoint}")]
    private static partial void LogTokenRefreshFailed(ILogger logger, Uri endpoint, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AllowInsecureTokenEndpoint is enabled: the OAuth client secret will be sent over plaintext http to {Endpoint}, where anyone on the network path can read it. Use an https token endpoint for anything beyond local development.")]
    private static partial void LogInsecureTokenEndpoint(ILogger logger, Uri endpoint);

    /// <inheritdoc />
    public void Dispose()
    {
        _refreshLock.Dispose();
    }
}
