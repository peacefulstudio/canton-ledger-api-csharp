// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Authentication;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

internal sealed class LocalnetTokenProvider(Func<CancellationToken, ValueTask<string>> getToken) : ITokenProvider
{
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
        => await getToken(cancellationToken).ConfigureAwait(false);
}
