// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Peaceful.Extensions.Logging;

namespace Canton.Ledger.Auth.TokenGeneration;

/// <summary>
/// OAuth2 client-credentials token provider with TTL-based caching.
/// </summary>
public sealed partial class ClientCredentialsProvider : ITokenProvider, IDisposable
{
    private static readonly ILogger<ClientCredentialsProvider> Logger = StaticLoggerFactory.Create<ClientCredentialsProvider>();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ClientCredentialsOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private string? _cachedToken;
    private long _expiresAtTicks;

    /// <summary>
    /// Creates a new <see cref="ClientCredentialsProvider"/>.
    /// </summary>
    public ClientCredentialsProvider(
        IOptions<ClientCredentialsOptions> options,
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        // Fast path: return cached token if still valid (volatile reads for cross-thread visibility)
        var cachedToken = Volatile.Read(ref _cachedToken);
        if (cachedToken is not null && _timeProvider.GetUtcNow().Ticks < Volatile.Read(ref _expiresAtTicks) - _options.SafetyMargin.Ticks)
            return cachedToken;

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            cachedToken = Volatile.Read(ref _cachedToken);
            if (cachedToken is not null && _timeProvider.GetUtcNow().Ticks < Volatile.Read(ref _expiresAtTicks) - _options.SafetyMargin.Ticks)
                return cachedToken;

            return await RequestTokenAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogTokenRefreshFailed(Logger, _options.TokenGenerationEndpoint, ex);
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
        using var response = await httpClient.PostAsync(
            _options.TokenGenerationEndpoint,
            content,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (errorBody.Length > 1024)
                errorBody = string.Concat(errorBody.AsSpan(0, 1024), "… (truncated)");
            LogTokenAcquisitionFailed(Logger, _options.TokenGenerationEndpoint, (int)response.StatusCode, errorBody);
            response.EnsureSuccessStatusCode();
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Token endpoint returned null response.");

        if (string.IsNullOrEmpty(tokenResponse.AccessToken))
            throw new InvalidOperationException(
                $"Token endpoint {_options.TokenGenerationEndpoint} returned a response with no access_token.");

        if (tokenResponse.ExpiresIn <= 0)
            throw new InvalidOperationException(
                $"Token endpoint {_options.TokenGenerationEndpoint} returned an invalid expires_in value '{tokenResponse.ExpiresIn}'. A positive value is required.");

        Volatile.Write(ref _cachedToken, tokenResponse.AccessToken);
        Volatile.Write(ref _expiresAtTicks, (_timeProvider.GetUtcNow() + TimeSpan.FromSeconds(tokenResponse.ExpiresIn)).Ticks);

        LogTokenAcquired(Logger, _options.TokenGenerationEndpoint);

        return tokenResponse.AccessToken;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Token acquired from {Endpoint}")]
    private static partial void LogTokenAcquired(ILogger logger, Uri endpoint);

    [LoggerMessage(Level = LogLevel.Error, Message = "Token acquisition failed from {Endpoint}: HTTP {StatusCode} — {ErrorBody}")]
    private static partial void LogTokenAcquisitionFailed(ILogger logger, Uri endpoint, int statusCode, string errorBody);

    [LoggerMessage(Level = LogLevel.Error, Message = "Token refresh failed from {Endpoint}")]
    private static partial void LogTokenRefreshFailed(ILogger logger, Uri endpoint, Exception exception);

    /// <inheritdoc />
    public void Dispose()
    {
        _refreshLock.Dispose();
    }
}
