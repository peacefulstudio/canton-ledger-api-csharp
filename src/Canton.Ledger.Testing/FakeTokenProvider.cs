// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;

namespace Canton.Ledger.Testing;

/// <summary>
/// An in-memory <see cref="ITokenProvider"/> test double that either resolves a canned bearer
/// token or faults with a configured exception, so business logic that authenticates through an
/// <see cref="ITokenProvider"/> can be unit-tested — including its auth-failure paths — without a
/// live identity provider and without a mocking framework.
/// </summary>
/// <remarks>
/// Unlike <see cref="FakeLedgerClient"/> and <see cref="FakePqsClient"/>, this fake has no
/// unconfigured-member surface to guard with <see cref="NotSupportedException"/>: it exposes a
/// single member, and both factories (<see cref="WithToken"/>, <see cref="WithFailure"/>) fully
/// configure it at construction. For a canned successful token outside of tests, see the
/// production <c>Canton.Ledger.Kernel.Authentication.StaticTokenProvider</c>, which this fake's
/// success variant mirrors; <see cref="FakeTokenProvider"/> additionally offers the failure
/// variant needed to exercise auth-failure paths.
/// </remarks>
public sealed class FakeTokenProvider : ITokenProvider
{
    private readonly Func<CancellationToken, Task<string>> _getTokenAsync;

    private FakeTokenProvider(Func<CancellationToken, Task<string>> getTokenAsync) => _getTokenAsync = getTokenAsync;

    /// <summary>Creates a fake whose <see cref="GetTokenAsync"/> always resolves to <paramref name="token"/>.</summary>
    /// <param name="token">The bearer token to return. Must not be null, empty, or whitespace.</param>
    /// <returns>The configured fake.</returns>
    public static FakeTokenProvider WithToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return new(_ => Task.FromResult(token));
    }

    /// <summary>
    /// Creates a fake whose <see cref="GetTokenAsync"/> always faults with
    /// <paramref name="exception"/>, for exercising auth-failure paths.
    /// </summary>
    /// <param name="exception">The exception the returned task faults with.</param>
    /// <returns>The configured fake.</returns>
    public static FakeTokenProvider WithFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new(_ => Task.FromException<string>(exception));
    }

    /// <inheritdoc />
    public Task<string> GetTokenAsync(CancellationToken cancellationToken = default) => _getTokenAsync(cancellationToken);
}
