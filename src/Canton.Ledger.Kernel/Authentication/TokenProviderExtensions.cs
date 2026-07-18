// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Kernel.Authentication;

/// <summary>
/// Shared token-resolution helpers so every transport applies the same
/// unauthenticated-skip and empty-token guards to an <see cref="ITokenProvider"/>.
/// </summary>
public static class TokenProviderExtensions
{
    /// <summary>
    /// Resolves the bearer token to send on an outgoing request, applying the guards
    /// shared by every transport: <see cref="ITokenProvider.None"/> (and a <see langword="null"/>
    /// provider) resolve to <see langword="null"/> so callers skip the Authorization header,
    /// and a provider that yields a blank token fails loudly instead of sending an
    /// unauthenticated request.
    /// </summary>
    /// <param name="tokenProvider">
    /// The provider of bearer tokens; <see cref="ITokenProvider.None"/> or <see langword="null"/> disables the header.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the token acquisition.</param>
    /// <returns>The bearer token, or <see langword="null"/> when no Authorization header should be sent.</returns>
    /// <exception cref="InvalidOperationException">The provider returned a null, empty, or whitespace token.</exception>
    public static async Task<string?> ResolveBearerTokenAsync(
        this ITokenProvider? tokenProvider,
        CancellationToken cancellationToken = default)
    {
        if (tokenProvider is null || ReferenceEquals(tokenProvider, ITokenProvider.None))
            return null;

        var token = await tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                $"Token provider {tokenProvider.GetType().Name} returned an empty token.");

        return token;
    }
}
