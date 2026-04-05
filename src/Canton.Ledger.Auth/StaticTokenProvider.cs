// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

namespace Canton.Ledger.Auth;

/// <summary>
/// Token provider that returns a fixed bearer token. Use this for short-lived processes
/// or environments where token refresh is not needed.
/// </summary>
public sealed class StaticTokenProvider : ITokenProvider
{
    private readonly string _token;

    /// <summary>
    /// Creates a new <see cref="StaticTokenProvider"/> with the specified token.
    /// </summary>
    /// <param name="token">The bearer token. Must not be null or empty.</param>
    public StaticTokenProvider(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _token = token;
    }

    /// <inheritdoc />
    public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_token);
}
