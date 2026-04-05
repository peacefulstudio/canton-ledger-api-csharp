// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

namespace Canton.Ledger.Auth;

/// <summary>
/// Provides bearer tokens for authenticating with Canton participant nodes.
/// </summary>
public interface ITokenProvider
{
    /// <summary>
    /// Returns a valid bearer token. Implementations may cache and refresh tokens automatically.
    /// </summary>
    Task<string> GetTokenAsync(CancellationToken cancellationToken = default);
}
