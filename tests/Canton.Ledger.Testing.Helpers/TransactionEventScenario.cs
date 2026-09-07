// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// Which event shape a transaction's event list carries. Each shape carries its own identity and
/// offset fields, which is what <see cref="ContractStreamProjectorParityTests"/> pins.
/// </summary>
public enum TransactionEventShape
{
    /// <summary>A created event, classifying as <c>Created</c> when it matches the marker.</summary>
    Created,

    /// <summary>An archived event, classifying as <c>Archived</c> when it matches the marker.</summary>
    Archived,

    /// <summary>An exercised event, classifying as <c>Exercised</c> when it matches the marker.</summary>
    Exercised,

    /// <summary>An event with no case set at all, which no marker can classify.</summary>
    Empty,
}

/// <summary>
/// A transport-neutral description of one transaction and the events it carries. Each transport's
/// parity subclass renders it into its own wire shape — protobuf messages for gRPC, a JSON body for
/// HTTP — so the shared assertions compare classification outcomes rather than encodings.
/// </summary>
public sealed record TransactionEventScenario
{
    /// <summary>The offset the transaction itself carries.</summary>
    public const long TransactionOffset = 70L;

    /// <summary>The offset the scenario's event carries.</summary>
    public const long EventOffset = 71L;

    /// <summary>The offset the trailing well-formed created event carries.</summary>
    public const long TrailingEventOffset = 72L;

    /// <summary>The choice name an exercised event reports.</summary>
    public const string ChoiceName = "Transfer";

    /// <summary>Which event shape to render.</summary>
    public required TransactionEventShape Event { get; init; }

    /// <summary>The event's entity name — the lever that decides whether the marker matches.</summary>
    public string EntityName { get; init; } = ActiveContractScenario.MatchingEntityName;

    /// <summary>The synchronizer the transaction carries, or <c>null</c> to omit it entirely.</summary>
    public string? Synchronizer { get; init; } = ActiveContractScenario.SynchronizerId;

    /// <summary>Renders the event with no template id, which every transport's decoder rejects.</summary>
    public bool OmitTemplateId { get; init; }

    /// <summary>
    /// Renders the event with no offset of its own — the unset field every transport encodes as
    /// zero — so a resume offset read off the event rather than off the transaction is visible as
    /// the beginning of the ledger.
    /// </summary>
    public bool OmitEventOffset { get; init; }

    /// <summary>Appends a well-formed matching created event after the scenario's own event.</summary>
    public bool FollowedByMatchingCreated { get; init; }
}
