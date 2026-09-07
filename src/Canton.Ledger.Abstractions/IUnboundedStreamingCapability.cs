// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Abstractions;

/// <summary>
/// Reports whether a ledger client serves an open-ended live tail — a subscription with no end
/// offset — so a consumer holding the DI-registered <see cref="ICantonLedgerClient"/> can ask before
/// it subscribes, rather than downcasting to a concrete client to find out.
/// </summary>
/// <remarks>
/// A separate capability interface rather than a member on <see cref="ICantonLedgerClient"/>: the
/// probe answers a question about one implementation, the way <see cref="System.IO.Stream.CanSeek"/>
/// does, and widening the client contract would break every external implementor.
/// </remarks>
public interface IUnboundedStreamingCapability
{
    /// <summary>
    /// <see langword="true"/> when this client serves a subscription whose end offset is
    /// <see langword="null"/>; <see langword="false"/> when such a call throws
    /// <see cref="NotSupportedException"/> and only a bounded offset range is available.
    /// </summary>
    /// <remarks>
    /// The value is a statement about this implementation, not about its transport: a client may
    /// report <see langword="false"/> because it has not yet been extended to consume an unbounded
    /// tail, even where the participant would serve one. Read it as a per-instance capability probe
    /// and pair it with the subscription call it governs.
    /// </remarks>
    bool SupportsUnboundedStreaming { get; }
}
