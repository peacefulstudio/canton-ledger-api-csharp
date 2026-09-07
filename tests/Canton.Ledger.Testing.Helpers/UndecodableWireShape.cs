// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// A transaction body a participant can legally put on the wire and that no transport can decode:
/// the encoding permits the value, the Daml-LF type it decodes into does not. Each transport's
/// point-read parity suite builds these in its own wire vocabulary, so both are held to the same
/// verdict — the participant sent something unreadable, which is a malformed response and not a bug
/// of ours.
/// </summary>
public enum UndecodableWireShape
{
    /// <summary>An offset below zero, which no <c>LedgerOffset</c> can carry.</summary>
    NegativeOffset,

    /// <summary>An exercised event acting as the empty party, which no <c>Party</c> can carry.</summary>
    EmptyActingParty,

    /// <summary>
    /// A command id of nothing but whitespace, which no <c>CommandId</c> can carry. Distinct from an
    /// absent command id, which both transports project as <c>null</c> and which stays readable.
    /// </summary>
    WhitespaceCommandId,
}
