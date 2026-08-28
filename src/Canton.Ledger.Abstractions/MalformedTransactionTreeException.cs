// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Abstractions;

/// <summary>
/// Thrown when the node ids on a transaction's events cannot describe a tree, so no
/// <see cref="Daml.Runtime.Contracts.TransactionTree"/> can be reconstructed from them.
/// </summary>
/// <remarks>
/// A participant reports hierarchy implicitly: each exercise event states the highest node id in the
/// subtree it caused, and every event whose node id falls in that range is a descendant. That encoding
/// only yields a tree when the node ids honour it — they ascend strictly, every exercise says where its
/// subtree ends, that end does not precede the exercise itself, no subtree runs past the end of the one
/// enclosing it, and every event is a create or an exercise, the only shapes a ledger-effects transaction
/// reports. When any of those is broken the events still describe *something*, but not the hierarchy the
/// ledger committed, so reconstruction fails rather than guessing.
/// <para>
/// Both transports throw this one type: a tree that cannot be rebuilt is the same failure whether the
/// events arrived over gRPC or over the JSON Ledger API, so a consumer catches it once.
/// </para>
/// </remarks>
public sealed class MalformedTransactionTreeException : InvalidOperationException
{
    /// <summary>Creates an exception describing why the events could not be assembled into a tree.</summary>
    public MalformedTransactionTreeException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception describing why the events could not be assembled into a tree.</summary>
    public MalformedTransactionTreeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
