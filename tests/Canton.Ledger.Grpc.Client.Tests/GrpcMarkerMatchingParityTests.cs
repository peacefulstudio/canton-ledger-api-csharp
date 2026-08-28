// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Testing.Helpers;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using ProtoArchivedEvent = Com.Daml.Ledger.Api.V2.ArchivedEvent;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class GrpcMarkerMatchingParityTests : MarkerMatchingParityTests
{
    protected override bool IsInterfaceMarker(DamlTypeKind marker) => marker switch
    {
        DamlTypeKind.Template => MarkerMatcher<TemplateMarker>.IsInterface,
        DamlTypeKind.Interface => MarkerMatcher<InterfaceMarker>.IsInterface,
        _ => throw new ArgumentOutOfRangeException(nameof(marker)),
    };

    protected override RuntimeIdentifier StreamFilterIdentifier(DamlTypeKind marker)
    {
        var identifier = marker switch
        {
            DamlTypeKind.Template => MarkerMatcher<TemplateMarker>.StreamFilterIdentifier(),
            DamlTypeKind.Interface => MarkerMatcher<InterfaceMarker>.StreamFilterIdentifier(),
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
        DamlTypeKind.Template => MarkerMatcher<TemplateMarker>.Matches(created),
        DamlTypeKind.Interface => MarkerMatcher<InterfaceMarker>.Matches(created),
        _ => throw new ArgumentOutOfRangeException(nameof(marker)),
    };

    private static bool MatchesWireEvent<TMarker>(MarkerMatchScenario scenario)
        where TMarker : Daml.Runtime.IDamlType =>
        scenario.Event switch
        {
            MarkerWireEvent.Created => MarkerMatcher<TMarker>.MatchesProtoCreated(BuildCreated(scenario)),
            MarkerWireEvent.Archived => MarkerMatcher<TMarker>.MatchesProtoArchived(BuildArchived(scenario)),
            MarkerWireEvent.Exercised => MarkerMatcher<TMarker>.MatchesProtoExercised(BuildExercised(scenario)),
            MarkerWireEvent.Unassigned => MarkerMatcher<TMarker>.MatchesProtoUnassigned(BuildUnassigned(scenario)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

    private static ProtoCreatedEvent BuildCreated(MarkerMatchScenario scenario)
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = ContractId,
            TemplateId = ToProto(scenario.TemplateId),
        };
        if (scenario.ImplementedInterface is { } implemented)
        {
            created.InterfaceViews.Add(new InterfaceView { InterfaceId = ToProto(implemented) });
        }
        return created;
    }

    private static ProtoArchivedEvent BuildArchived(MarkerMatchScenario scenario)
    {
        var archived = new ProtoArchivedEvent
        {
            ContractId = ContractId,
            TemplateId = ToProto(scenario.TemplateId),
        };
        if (scenario.ImplementedInterface is { } implemented)
        {
            archived.ImplementedInterfaces.Add(ToProto(implemented));
        }
        return archived;
    }

    private static ProtoExercisedEvent BuildExercised(MarkerMatchScenario scenario)
    {
        var exercised = new ProtoExercisedEvent
        {
            ContractId = ContractId,
            TemplateId = ToProto(scenario.TemplateId),
        };
        if (scenario.ImplementedInterface is { } implemented)
        {
            exercised.ImplementedInterfaces.Add(ToProto(implemented));
        }
        return exercised;
    }

    private static UnassignedEvent BuildUnassigned(MarkerMatchScenario scenario) => new()
    {
        ContractId = ContractId,
        TemplateId = ToProto(scenario.TemplateId),
    };

    private static ProtoIdentifier ToProto(RuntimeIdentifier identifier) => new()
    {
        PackageId = identifier.PackageId,
        ModuleName = identifier.ModuleName,
        EntityName = identifier.EntityName,
    };

    private const string ContractId = "00holding";
}
