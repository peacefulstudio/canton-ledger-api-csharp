// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Abstractions;

/// <summary>
/// A bounded page of PQS query results: at most <see cref="Limit"/> rows, starting
/// <see cref="Offset"/> rows into the match set.
/// </summary>
/// <remarks>
/// <para>
/// Translated to <c>LIMIT</c>/<c>OFFSET</c> on the PQS SQL query itself, so each round-trip
/// returns at most <see cref="Limit"/> rows instead of the unbounded match set. Paged queries
/// are ordered by <c>contract_id</c> to keep page boundaries stable across requests against an
/// unchanged active contract set.
/// </para>
/// <para>
/// <c>OFFSET</c> cost grows linearly with the number of skipped rows, so very deep paging over
/// large match sets is better served by narrowing the filter than by a large <see cref="Offset"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var thirdPage = await pqs.QueryAsync&lt;Agreement&gt;(new PqsPage(limit: 50, offset: 100), ct);
/// </code>
/// </example>
public sealed record PqsPage
{
    /// <summary>
    /// Creates a page request.
    /// </summary>
    /// <param name="limit">Maximum number of rows to return. Must be positive.</param>
    /// <param name="offset">
    /// Number of matching rows to skip before the page starts. Must not be negative.
    /// Defaults to 0 (the first page).
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="limit"/> is zero or negative, or <paramref name="offset"/> is negative.
    /// </exception>
    public PqsPage(int limit, int offset = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        Limit = limit;
        Offset = offset;
    }

    /// <summary>Maximum number of rows the page may contain.</summary>
    public int Limit { get; }

    /// <summary>Number of matching rows skipped before the page starts.</summary>
    public int Offset { get; }
}
