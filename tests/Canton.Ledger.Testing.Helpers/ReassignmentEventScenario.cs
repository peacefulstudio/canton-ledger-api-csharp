// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// Which event shape a reassignment's event list carries. Both shapes are scoped by a source and a
/// target synchronizer rather than by a single one, which is what
/// <see cref="ContractStreamProjectorParityTests"/> pins.
/// </summary>
public enum ReassignmentEventShape
{
    /// <summary>An assigned event, whose payload rides on its own created event.</summary>
    Assigned,

    /// <summary>An unassigned event, which carries its identity directly.</summary>
    Unassigned,

    /// <summary>An event with no case set at all, which no marker can classify.</summary>
    Empty,
}

/// <summary>
/// A transport-neutral description of one reassignment and the events it carries. Each transport's
/// parity subclass renders it into its own wire shape — protobuf messages for gRPC, a JSON body for
/// HTTP — so the shared assertions compare classification outcomes rather than encodings.
/// </summary>
public sealed record ReassignmentEventScenario
{
    /// <summary>The offset the reassignment itself carries.</summary>
    public const long ReassignmentOffset = 80L;

    /// <summary>The offset the scenario's event carries.</summary>
    public const long EventOffset = 81L;

    /// <summary>The offset the trailing well-formed unassigned event carries.</summary>
    public const long TrailingEventOffset = 82L;

    /// <summary>The reassignment id the event carries.</summary>
    public const string ReassignmentId = "reassignment-2";

    /// <summary>The reassignment counter the event carries.</summary>
    public const long ReassignmentCounter = 3L;

    /// <summary>Which event shape to render.</summary>
    public required ReassignmentEventShape Event { get; init; }

    /// <summary>The event's entity name — the lever that decides whether the marker matches.</summary>
    public string EntityName { get; init; } = ActiveContractScenario.MatchingEntityName;

    /// <summary>The synchronizer the contract leaves, or <c>null</c> to omit it entirely.</summary>
    public string? Source { get; init; } = ActiveContractScenario.SynchronizerId;

    /// <summary>The synchronizer the contract arrives on, or <c>null</c> to omit it entirely.</summary>
    public string? Target { get; init; } = ActiveContractScenario.CounterpartSynchronizerId;

    /// <summary>Renders an <see cref="ReassignmentEventShape.Assigned"/> event with no created event.</summary>
    public bool OmitCreatedEvent { get; init; }

    /// <summary>Renders the event with no template id, which every transport's decoder rejects.</summary>
    public bool OmitTemplateId { get; init; }

    /// <summary>
    /// Renders the event with no offset of its own — the unset field every transport encodes as
    /// zero — so a resume offset read off the event rather than off the reassignment is visible as
    /// the beginning of the ledger.
    /// </summary>
    public bool OmitEventOffset { get; init; }

    /// <summary>Appends a well-formed matching unassigned event after the scenario's own event.</summary>
    public bool FollowedByMatchingUnassigned { get; init; }
}
