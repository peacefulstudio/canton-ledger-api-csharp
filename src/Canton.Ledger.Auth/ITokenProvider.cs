// Copyright © 2026 Peaceful Studio OÜ. All rights reserved.

namespace Canton.Ledger.Auth;

/// <summary>
/// Provides bearer tokens for authenticating with Canton participant nodes.
/// </summary>
public interface ITokenProvider
{
    /// <summary>
    /// A token provider that signals unauthenticated access. The built-in clients
    /// detect this instance and skip the Authorization header entirely.
    /// </summary>
    static ITokenProvider None => NullTokenProvider.Instance;

    /// <summary>
    /// Returns a valid bearer token. Implementations may cache and refresh tokens automatically.
    /// </summary>
    Task<string> GetTokenAsync(CancellationToken cancellationToken = default);
}
