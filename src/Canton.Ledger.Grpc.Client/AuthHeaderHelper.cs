// Copyright 2026 Peaceful Studio OÜ

using Canton.Ledger.Auth;
using Grpc.Core;

namespace Canton.Ledger.Grpc.Client;

internal static class AuthHeaderHelper
{
    internal static async Task<Metadata?> GetHeadersAsync(ITokenProvider? tokenProvider, CancellationToken cancellationToken)
    {
        if (tokenProvider is null || ReferenceEquals(tokenProvider, ITokenProvider.None))
            return null;

        var token = await tokenProvider.GetTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                $"Token provider {tokenProvider.GetType().Name} returned an empty token.");

        return new Metadata
        {
            { "authorization", $"Bearer {token}" }
        };
    }
}
