// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Testing.Helpers;
using Daml.Runtime.Contracts;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using WireArchivedEvent = Canton.Ledger.Rest.Client.Raw.ArchivedEvent;
using WireCreatedEvent = Canton.Ledger.Rest.Client.Raw.CreatedEvent;
using WireExercisedEvent = Canton.Ledger.Rest.Client.Raw.ExercisedEvent;
using WireIdentifier = Canton.Ledger.Rest.Client.Raw.Identifier;
using WireInterfaceView = Canton.Ledger.Rest.Client.Raw.InterfaceView;
using WireUnassignedEvent = Canton.Ledger.Rest.Client.Raw.UnassignedEvent;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestMarkerMatchingParityTests : MarkerMatchingParityTests
{
    private const string ContractId = "00holding";

    protected override bool IsInterfaceMarker(DamlTypeKind marker) => marker switch
    {
        DamlTypeKind.Template => RestMarkerMatcher<TemplateMarker>.IsInterface,
        DamlTypeKind.Interface => RestMarkerMatcher<InterfaceMarker>.IsInterface,
        _ => throw new ArgumentOutOfRangeException(nameof(marker)),
    };

    protected override RuntimeIdentifier StreamFilterIdentifier(DamlTypeKind marker)
    {
        var identifier = marker switch
        {
            DamlTypeKind.Template => RestMarkerMatcher<TemplateMarker>.FilterIdentifier,
            DamlTypeKind.Interface => RestMarkerMatcher<InterfaceMarker>.FilterIdentifier,
            _ => throw new ArgumentOutOfRangeException(nameof(marker)),
        };
        return new RuntimeIdentifier(identifier.PackageId, identifier.ModuleName, identifier.EntityName);
    }

    protected override bool MatchesWireEvent(DamlTypeKind marker, MarkerMatchScenario scenario) => marker switch
    {
        DamlTypeKind.Template => MatchesWireEvent<TemplateMarker>(scenario),
        DamlTypeKind.Interface => MatchesWireEvent<InterfaceMarker>(scenario),
        _ => throw new ArgumentOutOfRangeException(nameof(marker)),
    };

    protected override bool MatchesCreatedContract(DamlTypeKind marker, CreatedContract created) => marker switch
    {
        DamlTypeKind.Template => RestMarkerMatcher<TemplateMarker>.MatchesContract(created),
        DamlTypeKind.Interface => RestMarkerMatcher<InterfaceMarker>.MatchesContract(created),
        _ => throw new ArgumentOutOfRangeException(nameof(marker)),
    };

    private static bool MatchesWireEvent<TMarker>(MarkerMatchScenario scenario)
        where TMarker : Daml.Runtime.IDamlType =>
        scenario.Event switch
        {
            MarkerWireEvent.Created => RestMarkerMatcher<TMarker>.MatchesCreated(BuildCreated(scenario)),
            MarkerWireEvent.Archived => RestMarkerMatcher<TMarker>.MatchesArchived(BuildArchived(scenario)),
            MarkerWireEvent.Exercised => RestMarkerMatcher<TMarker>.MatchesExercised(BuildExercised(scenario)),
            MarkerWireEvent.Unassigned => RestMarkerMatcher<TMarker>.MatchesUnassigned(BuildUnassigned(scenario)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

    private static WireCreatedEvent BuildCreated(MarkerMatchScenario scenario) => new()
    {
        ContractId = ContractId,
        TemplateId = ToWire(scenario.TemplateId),
        InterfaceViews = scenario.ImplementedInterface is { } implemented
            ? [new WireInterfaceView { InterfaceId = ToWire(implemented) }]
            : [],
    };

    private static WireArchivedEvent BuildArchived(MarkerMatchScenario scenario) => new()
    {
        ContractId = ContractId,
        TemplateId = ToWire(scenario.TemplateId),
        ImplementedInterfaces = scenario.ImplementedInterface is { } implemented ? [ToWire(implemented)] : [],
    };

    private static WireExercisedEvent BuildExercised(MarkerMatchScenario scenario) => new()
    {
        ContractId = ContractId,
        TemplateId = ToWire(scenario.TemplateId),
        ImplementedInterfaces = scenario.ImplementedInterface is { } implemented ? [ToWire(implemented)] : [],
    };

    private static WireUnassignedEvent BuildUnassigned(MarkerMatchScenario scenario) => new()
    {
        ContractId = ContractId,
        TemplateId = ToWire(scenario.TemplateId),
    };

    private static WireIdentifier ToWire(RuntimeIdentifier identifier) => new()
    {
        PackageId = identifier.PackageId,
        ModuleName = identifier.ModuleName,
        EntityName = identifier.EntityName,
    };
}
