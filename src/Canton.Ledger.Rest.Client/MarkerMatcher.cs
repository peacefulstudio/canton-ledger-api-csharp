// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using WireArchivedEvent = Canton.Ledger.Rest.Client.Raw.ArchivedEvent;
using WireCreatedEvent = Canton.Ledger.Rest.Client.Raw.CreatedEvent;
using WireExercisedEvent = Canton.Ledger.Rest.Client.Raw.ExercisedEvent;
using WireIdentifier = Canton.Ledger.Rest.Client.Raw.Identifier;
using WireUnassignedEvent = Canton.Ledger.Rest.Client.Raw.UnassignedEvent;

namespace Canton.Ledger.Rest.Client;

internal static class MarkerMatcher<TMarker>
    where TMarker : IDamlType
{
    /// <summary>
    /// The reassignment filter the JSON Ledger API accepts already scopes an interface marker's
    /// <c>Unassigned</c> events server-side — mirrors the gRPC transport's identically-named
    /// constant in <c>DamlMarker</c>.
    /// </summary>
    private const bool InterfaceFilterAlreadyScopesUnassignedEventsServerSide = true;

    private const int GoogleRpcCodeOk = 0;

    public static bool IsInterface { get; } = TMarker.DamlTypeId.Kind == DamlTypeKind.Interface;

    private static readonly RuntimeIdentifier MarkerIdentity = TMarker.DamlTypeId.Identifier;

    /// <summary>
    /// The wire <see cref="WireIdentifier"/> used to scope a <c>TemplateFilter</c> or
    /// <c>InterfaceFilter</c> to this marker, using the package-name reference format
    /// (<c>#&lt;package-name&gt;</c>) the JSON Ledger API accepts in place of a package id.
    /// </summary>
    public static WireIdentifier FilterIdentifier { get; } = new()
    {
        PackageId = $"#{TMarker.DamlTypeId.PackageName}",
        ModuleName = MarkerIdentity.ModuleName,
        EntityName = MarkerIdentity.EntityName,
    };

    public static bool MatchesCreated(WireCreatedEvent created) =>
        IsInterface
            ? MatchesAnyIdentifier(created.InterfaceViews?.Select(view => view?.InterfaceId))
            : RestWireConversions.IsModuleEntityMatch(created.TemplateId, MarkerIdentity);

    /// <summary>
    /// Reads the participant-computed view this interface marker's <c>InterfaceFilter</c> asked for
    /// off a wire created event — mirrors the gRPC transport's identically-named member on
    /// <c>DamlMarker</c>. Returns <see langword="false"/> when the matched view carries no
    /// <c>viewValue</c> or a <c>viewStatus</c> the participant did not report as <c>OK</c>.
    /// </summary>
    public static bool TryGetInterfaceViewRecord(WireCreatedEvent created, out DamlRecord record)
    {
        if (!IsInterface)
        {
            throw new InvalidOperationException(
                $"{typeof(TMarker).FullName} is not a Daml interface marker; interface views only apply to IDamlInterface markers.");
        }

        foreach (var view in created.InterfaceViews ?? [])
        {
            if (view is null || !RestWireConversions.IsModuleEntityMatch(view.InterfaceId, MarkerIdentity)) continue;

            if (view.ViewStatus?.Code != GoogleRpcCodeOk || view.ViewValue is null)
            {
                record = null!;
                return false;
            }

            record = RestValueDecoder.ToDamlRecord(view.ViewValue);
            return true;
        }

        record = null!;
        return false;
    }

    public static bool MatchesArchived(WireArchivedEvent archived) =>
        IsInterface
            ? MatchesAnyIdentifier(archived.ImplementedInterfaces?.Select(id => (WireIdentifier?)id))
            : RestWireConversions.IsModuleEntityMatch(archived.TemplateId, MarkerIdentity);

    public static bool MatchesExercised(WireExercisedEvent exercised) =>
        IsInterface
            ? MatchesAnyIdentifier(exercised.ImplementedInterfaces?.Select(id => (WireIdentifier?)id))
            : RestWireConversions.IsModuleEntityMatch(exercised.TemplateId, MarkerIdentity);

    public static bool MatchesUnassigned(WireUnassignedEvent unassigned) =>
        (IsInterface && InterfaceFilterAlreadyScopesUnassignedEventsServerSide)
            || RestWireConversions.IsModuleEntityMatch(unassigned.TemplateId, MarkerIdentity);

    /// <summary>
    /// Matches a transport-neutral <see cref="CreatedContract"/> (as projected from a command
    /// submission's transaction result) against this marker, for use by
    /// <c>RestTransactionResultProjector.ProjectToContractId</c>.
    /// </summary>
    public static bool MatchesContract(CreatedContract created) =>
        IsInterface
            ? created.InterfaceIds.Any(id => RestWireConversions.IsModuleEntityMatch(id, MarkerIdentity))
            : RestWireConversions.IsModuleEntityMatch(created.TemplateId, MarkerIdentity);

    private static bool MatchesAnyIdentifier(IEnumerable<WireIdentifier?>? identifiers)
    {
        if (identifiers is null) return false;
        foreach (var identifier in identifiers)
        {
            if (RestWireConversions.IsModuleEntityMatch(identifier, MarkerIdentity)) return true;
        }
        return false;
    }
}
