// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

namespace Canton.Ledger.Auth;

/// <summary>
/// Token provider that signals unauthenticated access. Clients receiving this
/// provider skip the Authorization header entirely.
/// </summary>
internal sealed class NullTokenProvider : ITokenProvider
{
    internal static readonly NullTokenProvider Instance = new();

    private NullTokenProvider() { }

    public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(string.Empty);
}
