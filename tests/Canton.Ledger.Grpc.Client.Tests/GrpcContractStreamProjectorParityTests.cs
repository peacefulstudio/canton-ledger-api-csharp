// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Testing.Helpers;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Streams;
using Google.Rpc;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class GrpcContractStreamProjectorParityTests : ContractStreamProjectorParityTests
{
    protected override Task<IReadOnlyList<ContractStreamEvent<TemplateMarker>>> ProjectActiveContractEntryAsync(
        ActiveContractScenario scenario)
    {
        var response = BuildResponse(scenario);
        IReadOnlyList<ContractStreamEvent<TemplateMarker>> projected =
            ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response).ToList();
        return Task.FromResult(projected);
    }

    protected override Task<IReadOnlyList<ContractStreamEvent<InterfaceMarker>>> ProjectActiveContractEntryAsInterfaceAsync(
        ActiveContractScenario scenario)
    {
        var response = BuildResponse(scenario);
        IReadOnlyList<ContractStreamEvent<InterfaceMarker>> projected =
            ContractStreamProjector.ProjectActiveContractEntry<InterfaceMarker>(response).ToList();
        return Task.FromResult(projected);
    }

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
                        ContractId = ActiveContractScenario.ContractId,
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
                Label = ActiveContractScenario.PayloadFieldName,
                Value = new ProtoValue { Text = value },
            },
        },
    };
}
