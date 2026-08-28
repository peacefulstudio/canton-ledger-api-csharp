// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;

namespace Canton.Ledger.Testing.Localnet;

/// <summary>
/// Adapts a localnet token source (e.g. <c>Peaceful.Canton.Localnet.Testing</c>'s
/// <c>OAuth2TokenProvider.GetAccessTokenAsync</c>) onto this repo's <see cref="ITokenProvider"/>.
/// </summary>
public sealed class LocalnetTokenProvider(Func<CancellationToken, ValueTask<string>> getToken) : ITokenProvider
{
    /// <inheritdoc />
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
        => await getToken(cancellationToken).ConfigureAwait(false);
}
