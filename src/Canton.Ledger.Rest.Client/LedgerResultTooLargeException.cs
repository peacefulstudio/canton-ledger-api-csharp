// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Thrown when a bounded read over the Canton JSON Ledger API — a
/// <c>POST /v2/state/active-contracts</c> snapshot or a <c>POST /v2/updates</c> offset-range
/// read — returns <c>413 Content Too Large</c> because the result would exceed the
/// participant's <c>http-list-max-elements-limit</c>. Narrow the requested offset range
/// (a smaller <c>toOffset</c>, or a more recent <c>activeAtOffset</c>), or use a future
/// WebSocket transport.
/// </summary>
public sealed class LedgerResultTooLargeException : Exception
{
    /// <summary>Initializes a new instance with the given message.</summary>
    public LedgerResultTooLargeException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the given message and inner exception.</summary>
    public LedgerResultTooLargeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
