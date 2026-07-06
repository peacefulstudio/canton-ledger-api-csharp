// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Kernel.Authentication;

internal sealed class NullTokenProvider : ITokenProvider
{
    internal static readonly NullTokenProvider Instance = new();

    private NullTokenProvider() { }

    public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(string.Empty);
}
