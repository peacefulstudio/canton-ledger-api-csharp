// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// Which <c>contract_entry</c> shape the participant delivered on the active-contract snapshot.
/// Each shape carries its synchronizer in a different field, which is exactly what
/// <see cref="ContractStreamProjectorParityTests"/> pins.
/// </summary>
public enum ActiveContractEntry
{
    /// <summary>An <c>ActiveContract</c>, whose synchronizer is its own <c>synchronizer_id</c>.</summary>
    Active,

    /// <summary>An <c>IncompleteUnassigned</c>, scoped to its unassigned event's <c>source</c>.</summary>
    IncompleteUnassigned,

    /// <summary>An <c>IncompleteAssigned</c>, scoped to its assigned event's <c>target</c>.</summary>
    IncompleteAssigned,
}

/// <summary>
/// Which interface view the participant attached to an entry's created event, alongside the
/// implementing template's own create argument. Only the interface-marker lane of
/// <see cref="ContractStreamProjectorParityTests"/> turns this lever.
/// </summary>
public enum InterfaceViewRendering
{
    /// <summary>No interface view at all, leaving an interface marker nothing to match.</summary>
    None,

    /// <summary>A view the participant computed, carrying <see cref="ActiveContractScenario.InterfaceViewValue"/>.</summary>
    Computed,

    /// <summary>A view whose <c>view_status</c> reports the participant could not compute it.</summary>
    ComputationFailed,

    /// <summary>A view whose <c>view_status</c> reports success yet which carries no view value.</summary>
    ValueOmitted,
}

/// <summary>
/// A transport-neutral description of one active-contract entry. Each transport's parity subclass
/// renders it into its own wire shape — protobuf messages for gRPC, a JSON body for HTTP — so the
/// shared assertions compare classification outcomes rather than encodings.
/// </summary>
public sealed record ActiveContractScenario
{
    /// <summary>The wire fixture values both transports render, so their outcomes are comparable.</summary>
    public const string ContractId = "00holding";

    /// <summary>The module name of <see cref="TemplateMarker"/>.</summary>
    public static string ModuleName { get; } = TemplateMarker.TemplateId.ModuleName;

    /// <summary>The entity name that classifies as <see cref="TemplateMarker"/>.</summary>
    public static string MatchingEntityName { get; } = TemplateMarker.TemplateId.EntityName;

    /// <summary>An entity name in the same module that does not classify as <see cref="TemplateMarker"/>.</summary>
    public const string OtherEntityName = "Other";

    /// <summary>The module name of <see cref="InterfaceMarker"/>.</summary>
    public static string InterfaceModuleName { get; } = InterfaceMarker.InterfaceId.ModuleName;

    /// <summary>The entity name of <see cref="InterfaceMarker"/>.</summary>
    public static string InterfaceEntityName { get; } = InterfaceMarker.InterfaceId.EntityName;

    /// <summary>The package id of <see cref="InterfaceMarker"/>.</summary>
    public static string InterfacePackageId { get; } = InterfaceMarker.PackageId;

    /// <summary>The field both the create argument and the interface view carry, with different values.</summary>
    public const string PayloadFieldName = "amount";

    /// <summary>The value the implementing template's create argument carries.</summary>
    public const string CreateArgumentValue = "create-argument-value";

    /// <summary>The value the participant-computed interface view carries.</summary>
    public const string InterfaceViewValue = "interface-view-value";

    /// <summary>The <c>view_status</c> code a successfully computed interface view reports.</summary>
    public const int ComputedViewStatusCode = 0;

    /// <summary>The <c>view_status</c> code a failed interface-view computation reports.</summary>
    public const int FailedViewStatusCode = 2;

    /// <summary>The offset the created event carries.</summary>
    public const long CreatedOffset = 42L;

    /// <summary>The synchronizer the entry carries, wherever its shape happens to put it.</summary>
    public const string SynchronizerId = "sync-1";

    /// <summary>The synchronizer on the far side of an entry's in-flight reassignment.</summary>
    public const string CounterpartSynchronizerId = "sync-counterpart";

    /// <summary>The offset the unassigned event of an <see cref="ActiveContractEntry.IncompleteUnassigned"/> carries.</summary>
    public const long UnassignedOffset = 43L;

    /// <summary>The reassignment id the unassigned event carries.</summary>
    public const string ReassignmentId = "reassignment-1";

    /// <summary>The reassignment counter the unassigned event carries.</summary>
    public const long ReassignmentCounter = 7L;

    /// <summary>Which contract-entry shape to render.</summary>
    public required ActiveContractEntry Entry { get; init; }

    /// <summary>The created event's entity name — the lever that decides whether the marker matches.</summary>
    public string EntityName { get; init; } = MatchingEntityName;

    /// <summary>The synchronizer the entry carries, or <c>null</c> to omit it entirely.</summary>
    public string? Synchronizer { get; init; } = SynchronizerId;

    /// <summary>Renders the entry with no created event at all.</summary>
    public bool OmitCreatedEvent { get; init; }

    /// <summary>The interface view the created event carries — the lever the interface-marker lane turns.</summary>
    public InterfaceViewRendering InterfaceView { get; init; } = InterfaceViewRendering.None;
}
