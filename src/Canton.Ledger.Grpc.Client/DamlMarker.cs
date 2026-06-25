// Copyright 2026 Peaceful Studio OÜ

using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Grpc;
using ProtoArchivedEvent = Com.Daml.Ledger.Api.V2.ArchivedEvent;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client;

internal static class MarkerMatcher<TMarker>
    where TMarker : IDamlType
{
    public static bool IsInterface { get; } = typeof(IDamlInterface).IsAssignableFrom(typeof(TMarker));

    private static readonly RuntimeIdentifier MarkerIdentity = ResolveMarkerIdentity();

    private static readonly string PackageName = MarkerPackageName();

    public static bool Matches(CreatedContract created) =>
        IsInterface
            ? created.InterfaceIds.Any(id => IsModuleEntityMatch(id, MarkerIdentity))
            : IsModuleEntityMatch(created.TemplateId, MarkerIdentity);

    public static ProtoIdentifier StreamFilterIdentifier() =>
        DamlValueConverter.ToProtoTemplateNameIdentifier(PackageName, MarkerIdentity);

    public static bool MatchesProtoCreated(ProtoCreatedEvent created)
    {
        if (IsInterface)
        {
            return MatchesAnyImplementedInterface(created.InterfaceViews.Select(view => view.InterfaceId));
        }

        return ContractStreamProjector.IsTemplateMatch(created.TemplateId, MarkerIdentity);
    }

    public static bool MatchesProtoArchived(ProtoArchivedEvent archived)
    {
        if (IsInterface)
        {
            return MatchesAnyImplementedInterface(archived.ImplementedInterfaces);
        }

        return ContractStreamProjector.IsTemplateMatch(archived.TemplateId, MarkerIdentity);
    }

    public static bool MatchesProtoExercised(ProtoExercisedEvent exercised)
    {
        if (IsInterface)
        {
            return MatchesAnyImplementedInterface(exercised.ImplementedInterfaces);
        }

        return ContractStreamProjector.IsTemplateMatch(exercised.TemplateId, MarkerIdentity);
    }

    public static bool MatchesProtoUnassigned(UnassignedEvent unassigned)
    {
        if (IsInterface)
        {
            return false;
        }

        return ContractStreamProjector.IsTemplateMatch(unassigned.TemplateId, MarkerIdentity);
    }

    private static bool MatchesAnyImplementedInterface(IEnumerable<ProtoIdentifier> implementedInterfaces)
    {
        foreach (var implemented in implementedInterfaces)
        {
            if (ContractStreamProjector.IsTemplateMatch(implemented, MarkerIdentity)) return true;
        }
        return false;
    }

    private static RuntimeIdentifier ResolveMarkerIdentity()
    {
        if (typeof(IDamlInterface).IsAssignableFrom(typeof(TMarker)))
        {
            return MarkerIdentifier(nameof(IDamlInterface.InterfaceId));
        }

        if (typeof(ITemplate).IsAssignableFrom(typeof(TMarker)))
        {
            return MarkerIdentifier(nameof(ITemplate.TemplateId));
        }

        throw new InvalidOperationException(
            $"{typeof(TMarker).FullName} implements IDamlType but is neither IDamlInterface nor ITemplate.");
    }

    private static RuntimeIdentifier MarkerIdentifier(string propertyName) =>
        (RuntimeIdentifier)ReadStaticMember(propertyName);

    private static string MarkerPackageName() =>
        (string)ReadStaticMember("PackageName");

    private static object ReadStaticMember(string propertyName)
    {
        var property = typeof(TMarker).GetProperty(propertyName)
            ?? throw new InvalidOperationException(
                $"Marker type {typeof(TMarker).FullName} does not expose static member '{propertyName}'.");
        return property.GetValue(null)
            ?? throw new InvalidOperationException(
                $"Static member '{propertyName}' on marker type {typeof(TMarker).FullName} returned null.");
    }

    private static bool IsModuleEntityMatch(RuntimeIdentifier candidate, RuntimeIdentifier expected) =>
        string.Equals(candidate.ModuleName, expected.ModuleName, StringComparison.Ordinal)
        && string.Equals(candidate.EntityName, expected.EntityName, StringComparison.Ordinal);
}
