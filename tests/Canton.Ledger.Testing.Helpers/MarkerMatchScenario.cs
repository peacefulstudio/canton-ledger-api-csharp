// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// Which wire event shape a marker-matching scenario is rendered as. The shapes differ in where
/// they carry the interface identities a marker can match through: a created event carries them as
/// interface views, an archived or exercised event as implemented interfaces, and an unassigned
/// event carries none at all.
/// </summary>
public enum MarkerWireEvent
{
    /// <summary>A created event, whose interface identities live in its interface views.</summary>
    Created,

    /// <summary>An archived event, whose interface identities live in its implemented interfaces.</summary>
    Archived,

    /// <summary>An exercised event, whose interface identities live in its implemented interfaces.</summary>
    Exercised,

    /// <summary>An unassigned event, which carries a template identity and no interface identities.</summary>
    Unassigned,
}

/// <summary>
/// A transport-neutral description of one wire event to match a marker against. Each transport's
/// parity subclass renders it into its own wire shape — protobuf messages for gRPC, generated wire
/// records for HTTP — so the shared assertions compare matching outcomes rather than encodings.
/// </summary>
public sealed record MarkerMatchScenario
{
    /// <summary>
    /// The package id every scenario identity carries. It is deliberately not the marker's own
    /// package: matching is by module and entity name, so the package id must never decide it.
    /// </summary>
    public const string UnrelatedPackageId = "unrelated-pkg";

    /// <summary><see cref="TemplateMarker"/>'s module and entity, in an unrelated package.</summary>
    public static RuntimeIdentifier MatchingTemplateId { get; } = new(
        UnrelatedPackageId, TemplateMarker.TemplateId.ModuleName, TemplateMarker.TemplateId.EntityName);

    /// <summary>A template identity in <see cref="TemplateMarker"/>'s module that must not match.</summary>
    public static RuntimeIdentifier OtherTemplateId { get; } = new(
        UnrelatedPackageId, TemplateMarker.TemplateId.ModuleName, "Other");

    /// <summary><see cref="InterfaceMarker"/>'s module and entity, in an unrelated package.</summary>
    public static RuntimeIdentifier MatchingInterfaceId { get; } = new(
        UnrelatedPackageId, InterfaceMarker.InterfaceId.ModuleName, InterfaceMarker.InterfaceId.EntityName);

    /// <summary>An interface identity in <see cref="InterfaceMarker"/>'s module that must not match.</summary>
    public static RuntimeIdentifier OtherInterfaceId { get; } = new(
        UnrelatedPackageId, InterfaceMarker.InterfaceId.ModuleName, "IOther");

    /// <summary>Which wire event shape to render.</summary>
    public required MarkerWireEvent Event { get; init; }

    /// <summary>The template identity the rendered event carries.</summary>
    public RuntimeIdentifier TemplateId { get; init; } = MatchingTemplateId;

    /// <summary>The interface identity the rendered event implements, or <c>null</c> to implement none.</summary>
    public RuntimeIdentifier? ImplementedInterface { get; init; }
}
