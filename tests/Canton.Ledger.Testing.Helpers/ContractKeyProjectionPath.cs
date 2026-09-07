// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// A path a transport takes from a wire created event to a projected contract key. Each builds its
/// own <c>ContractKey</c> from the same wire fields, so each is a place the key can be built
/// incompletely without any other path noticing.
/// </summary>
public enum ContractKeyProjectionPath
{
    /// <summary>The template-typed contract stream, read as a subscription's created event.</summary>
    ContractStream,

    /// <summary>The interface-typed contract stream, read as a subscription's created event.</summary>
    InterfaceStream,

    /// <summary>The flattened transaction result a command submission returns.</summary>
    TransactionResult,

    /// <summary>The transaction tree a command submission returns.</summary>
    TransactionTree,
}
