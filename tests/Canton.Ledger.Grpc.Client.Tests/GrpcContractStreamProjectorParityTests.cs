// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Testing.Helpers;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Streams;
using Google.Rpc;
using ProtoArchivedEvent = Com.Daml.Ledger.Api.V2.ArchivedEvent;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class GrpcContractStreamProjectorParityTests : ContractStreamProjectorParityTests
{
    private const long UnsetOffset = 0L;

    protected override Task<IReadOnlyList<ContractStreamEvent<TemplateMarker>>> ProjectActiveContractEntryAsync(
        ActiveContractScenario scenario)
    {
        var response = BuildResponse(scenario);
        IReadOnlyList<ContractStreamEvent<TemplateMarker>> projected =
            ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response).ToList();
        return Task.FromResult(projected);
    }

    protected override Task<IReadOnlyList<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>>> ProjectActiveContractEntryAsInterfaceAsync(
        ActiveContractScenario scenario)
    {
        var response = BuildResponse(scenario);
        IReadOnlyList<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>> projected =
            InterfaceStreamProjector.ProjectActiveContractEntry<InterfaceMarker, InterfaceMarkerView>(response).ToList();
        return Task.FromResult(projected);
    }

    protected override Task<IReadOnlyList<ContractStreamEvent<TemplateMarker>>> ProjectTransactionEventsAsync(
        TransactionEventScenario scenario)
    {
        IReadOnlyList<ContractStreamEvent<TemplateMarker>> projected = ContractStreamProjector
            .ProjectTransactionEvents<TemplateMarker>(BuildTransaction(scenario)).ToList();
        return Task.FromResult(projected);
    }

    protected override Task<IReadOnlyList<ContractStreamEvent<TemplateMarker>>> ProjectReassignmentEventsAsync(
        ReassignmentEventScenario scenario)
    {
        IReadOnlyList<ContractStreamEvent<TemplateMarker>> projected = ContractStreamProjector
            .ProjectReassignmentEvents<TemplateMarker>(BuildReassignment(scenario)).ToList();
        return Task.FromResult(projected);
    }

    private static Transaction BuildTransaction(TransactionEventScenario scenario)
    {
        var transaction = new Transaction
        {
            Offset = TransactionEventScenario.TransactionOffset,
            SynchronizerId = scenario.Synchronizer ?? string.Empty,
        };
        transaction.Events.Add(BuildTransactionEvent(scenario));
        if (scenario.FollowedByMatchingCreated)
        {
            transaction.Events.Add(new Event
            {
                Created = MatchingCreatedEvent(
                    ActiveContractScenario.MatchingEntityName, TransactionEventScenario.TrailingEventOffset),
            });
        }
        return transaction;
    }

    private static Event BuildTransactionEvent(TransactionEventScenario scenario)
    {
        var templateId = scenario.OmitTemplateId ? null : TemplateIdFor(scenario.EntityName);
        var eventOffset = scenario.OmitEventOffset ? UnsetOffset : TransactionEventScenario.EventOffset;
        return scenario.Event switch
        {
            TransactionEventShape.Created => new Event
            {
                Created = MatchingCreatedEvent(scenario.EntityName, eventOffset, scenario.OmitTemplateId),
            },
            TransactionEventShape.Archived => new Event
            {
                Archived = new ProtoArchivedEvent
                {
                    ContractId = ActiveContractScenario.ContractId,
                    TemplateId = templateId,
                    Offset = eventOffset,
                },
            },
            TransactionEventShape.Exercised => new Event
            {
                Exercised = new ProtoExercisedEvent
                {
                    ContractId = ActiveContractScenario.ContractId,
                    TemplateId = templateId,
                    Choice = TransactionEventScenario.ChoiceName,
                    Consuming = true,
                    Offset = eventOffset,
                },
            },
            TransactionEventShape.Empty => new Event(),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }

    private static Reassignment BuildReassignment(ReassignmentEventScenario scenario)
    {
        var reassignment = new Reassignment { Offset = ReassignmentEventScenario.ReassignmentOffset };
        reassignment.Events.Add(BuildReassignmentEvent(scenario));
        if (scenario.FollowedByMatchingUnassigned)
        {
            reassignment.Events.Add(new ReassignmentEvent
            {
                Unassigned = BuildUnassignedEvent(
                    new ReassignmentEventScenario { Event = ReassignmentEventShape.Unassigned },
                    ReassignmentEventScenario.TrailingEventOffset),
            });
        }
        return reassignment;
    }

    private static ReassignmentEvent BuildReassignmentEvent(ReassignmentEventScenario scenario)
    {
        var eventOffset = scenario.OmitEventOffset ? UnsetOffset : ReassignmentEventScenario.EventOffset;
        return scenario.Event switch
        {
            ReassignmentEventShape.Assigned => new ReassignmentEvent
            {
                Assigned = new AssignedEvent
                {
                    Source = scenario.Source ?? string.Empty,
                    Target = scenario.Target ?? string.Empty,
                    ReassignmentId = ReassignmentEventScenario.ReassignmentId,
                    ReassignmentCounter = (ulong)ReassignmentEventScenario.ReassignmentCounter,
                    CreatedEvent = scenario.OmitCreatedEvent
                        ? null
                        : MatchingCreatedEvent(scenario.EntityName, eventOffset, scenario.OmitTemplateId),
                },
            },
            ReassignmentEventShape.Unassigned => new ReassignmentEvent
            {
                Unassigned = BuildUnassignedEvent(scenario, eventOffset),
            },
            ReassignmentEventShape.Empty => new ReassignmentEvent(),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }

    private static UnassignedEvent BuildUnassignedEvent(ReassignmentEventScenario scenario, long offset) => new()
    {
        ContractId = ActiveContractScenario.ContractId,
        TemplateId = scenario.OmitTemplateId ? null : TemplateIdFor(scenario.EntityName),
        Source = scenario.Source ?? string.Empty,
        Target = scenario.Target ?? string.Empty,
        Offset = offset,
        ReassignmentId = ReassignmentEventScenario.ReassignmentId,
        ReassignmentCounter = (ulong)ReassignmentEventScenario.ReassignmentCounter,
    };

    private static ProtoCreatedEvent MatchingCreatedEvent(string entityName, long offset, bool omitTemplateId = false) => new()
    {
        ContractId = ActiveContractScenario.ContractId,
        TemplateId = omitTemplateId ? null : TemplateIdFor(entityName),
        CreateArguments = PayloadRecord(ActiveContractScenario.CreateArgumentValue),
        Offset = offset,
    };

    private static ProtoIdentifier TemplateIdFor(string entityName) => new()
    {
        PackageId = "tmpl-pkg",
        ModuleName = ActiveContractScenario.ModuleName,
        EntityName = entityName,
    };

    private static GetActiveContractsResponse BuildResponse(ActiveContractScenario scenario)
    {
        var created = scenario.OmitCreatedEvent ? null : BuildCreatedEvent(scenario);
        return scenario.Entry switch
        {
            ActiveContractEntry.Active => new GetActiveContractsResponse
            {
                ActiveContract = new ActiveContract
                {
                    CreatedEvent = created,
                    SynchronizerId = scenario.Synchronizer ?? string.Empty,
                },
            },
            ActiveContractEntry.IncompleteUnassigned => new GetActiveContractsResponse
            {
                IncompleteUnassigned = new IncompleteUnassigned
                {
                    CreatedEvent = created,
                    UnassignedEvent = new UnassignedEvent
                    {
                        ContractId = scenario.OmitUnassignedContractId ? string.Empty : ActiveContractScenario.ContractId,
                        Source = scenario.Synchronizer ?? string.Empty,
                        Target = ActiveContractScenario.CounterpartSynchronizerId,
                        Offset = ActiveContractScenario.UnassignedOffset,
                        ReassignmentId = ActiveContractScenario.ReassignmentId,
                        ReassignmentCounter = (ulong)ActiveContractScenario.ReassignmentCounter,
                    },
                },
            },
            ActiveContractEntry.IncompleteAssigned => new GetActiveContractsResponse
            {
                IncompleteAssigned = new IncompleteAssigned
                {
                    AssignedEvent = new AssignedEvent
                    {
                        CreatedEvent = created,
                        Source = ActiveContractScenario.CounterpartSynchronizerId,
                        Target = scenario.Synchronizer ?? string.Empty,
                    },
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }

    private static ProtoCreatedEvent BuildCreatedEvent(ActiveContractScenario scenario)
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = ActiveContractScenario.ContractId,
            TemplateId = new ProtoIdentifier
            {
                PackageId = "tmpl-pkg",
                ModuleName = ActiveContractScenario.ModuleName,
                EntityName = scenario.EntityName,
            },
            CreateArguments = PayloadRecord(ActiveContractScenario.CreateArgumentValue),
            Offset = ActiveContractScenario.CreatedOffset,
        };
        if (BuildInterfaceView(scenario.InterfaceView) is { } interfaceView)
        {
            created.InterfaceViews.Add(interfaceView);
        }
        return created;
    }

    private static InterfaceView? BuildInterfaceView(InterfaceViewRendering rendering) => rendering switch
    {
        InterfaceViewRendering.None => null,
        InterfaceViewRendering.Computed => new InterfaceView
        {
            InterfaceId = SubscribedInterfaceId,
            ViewStatus = new Status { Code = ActiveContractScenario.ComputedViewStatusCode },
            ViewValue = PayloadRecord(ActiveContractScenario.InterfaceViewValue),
        },
        InterfaceViewRendering.ComputationFailed => new InterfaceView
        {
            InterfaceId = SubscribedInterfaceId,
            ViewStatus = new Status { Code = ActiveContractScenario.FailedViewStatusCode },
        },
        InterfaceViewRendering.ValueOmitted => new InterfaceView
        {
            InterfaceId = SubscribedInterfaceId,
            ViewStatus = new Status { Code = ActiveContractScenario.ComputedViewStatusCode },
        },
        _ => throw new ArgumentOutOfRangeException(nameof(rendering)),
    };

    private static ProtoIdentifier SubscribedInterfaceId => new()
    {
        PackageId = ActiveContractScenario.InterfacePackageId,
        ModuleName = ActiveContractScenario.InterfaceModuleName,
        EntityName = ActiveContractScenario.InterfaceEntityName,
    };

    private static ProtoRecord PayloadRecord(string value) => new()
    {
        Fields =
        {
            new RecordField
            {
                Label = ActiveContractScenario.OwnerFieldName,
                Value = new ProtoValue { Party = ActiveContractScenario.OwnerParty },
            },
            new RecordField
            {
                Label = ActiveContractScenario.PayloadFieldName,
                Value = new ProtoValue { Text = value },
            },
        },
    };
}
