// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using System.Text.Json;
using Canton.Ledger.Rest.Client.Raw;
using Canton.Ledger.Testing.Helpers;
using Daml.Runtime.Streams;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestContractStreamProjectorParityTests : ContractStreamProjectorParityTests
{
    private const long UnsetOffset = 0L;

    protected override async Task<IReadOnlyList<ContractStreamEvent<TemplateMarker>>> ProjectActiveContractEntryAsync(
        ActiveContractScenario scenario)
    {
        var response = await ActiveContractsResponseAsync(scenario);

        return ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response).ToList();
    }

    protected override async Task<IReadOnlyList<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>>> ProjectActiveContractEntryAsInterfaceAsync(
        ActiveContractScenario scenario)
    {
        var response = await ActiveContractsResponseAsync(scenario);

        return InterfaceStreamProjector.ProjectActiveContractEntry<InterfaceMarker, InterfaceMarkerView>(response).ToList();
    }

    protected override Task<IReadOnlyList<ContractStreamEvent<TemplateMarker>>> ProjectTransactionEventsAsync(
        TransactionEventScenario scenario)
    {
        IReadOnlyList<ContractStreamEvent<TemplateMarker>> projected = ContractStreamProjector
            .ProjectTransactionEvents<TemplateMarker>(UpdateFrom(TransactionJson(scenario)).Transaction).ToList();
        return Task.FromResult(projected);
    }

    protected override Task<IReadOnlyList<ContractStreamEvent<TemplateMarker>>> ProjectReassignmentEventsAsync(
        ReassignmentEventScenario scenario)
    {
        IReadOnlyList<ContractStreamEvent<TemplateMarker>> projected = ContractStreamProjector
            .ProjectReassignmentEvents<TemplateMarker>(UpdateFrom(ReassignmentJson(scenario)).Reassignment).ToList();
        return Task.FromResult(projected);
    }

    private static Update UpdateFrom(string json) =>
        JsonSerializer.Deserialize<GetUpdatesResponse>(json, RestRefitSettings.SerializerOptions)!.Update;

    private static string TransactionJson(TransactionEventScenario scenario)
    {
        var trailing = scenario.FollowedByMatchingCreated
            ? $", {{\"CreatedEvent\": {CreatedEventJson(ActiveContractScenario.MatchingEntityName, TransactionEventScenario.TrailingEventOffset, omitTemplateId: false)}}}"
            : string.Empty;

        return $$"""
            {
              "update": {
                "Transaction": {
                  "value": {
                    "offset": "{{Wire(TransactionEventScenario.TransactionOffset)}}",
                    {{OptionalField("synchronizerId", scenario.Synchronizer)}}
                    "events": [{{TransactionEventJson(scenario)}}{{trailing}}]
                  }
                }
              }
            }
            """;
    }

    private static string TransactionEventJson(TransactionEventScenario scenario)
    {
        var eventOffset = scenario.OmitEventOffset ? UnsetOffset : TransactionEventScenario.EventOffset;
        return scenario.Event switch
        {
            TransactionEventShape.Created =>
                $$"""
                {"CreatedEvent": {{CreatedEventJson(scenario.EntityName, eventOffset, scenario.OmitTemplateId)}}}
                """,
            TransactionEventShape.Archived =>
                $$"""
                {
                  "ArchivedEvent": {
                    "offset": "{{Wire(eventOffset)}}",
                    "nodeId": 0,
                    "contractId": "{{ActiveContractScenario.ContractId}}",
                    {{TemplateIdField(scenario.EntityName, scenario.OmitTemplateId)}}
                    "witnessParties": []
                  }
                }
                """,
            TransactionEventShape.Exercised =>
                $$"""
                {
                  "ExercisedEvent": {
                    "offset": "{{Wire(eventOffset)}}",
                    "nodeId": 0,
                    "contractId": "{{ActiveContractScenario.ContractId}}",
                    {{TemplateIdField(scenario.EntityName, scenario.OmitTemplateId)}}
                    "choice": "{{TransactionEventScenario.ChoiceName}}",
                    "consuming": true,
                    "witnessParties": []
                  }
                }
                """,
            TransactionEventShape.Empty => "{}",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }

    private static string ReassignmentJson(ReassignmentEventScenario scenario)
    {
        var trailing = scenario.FollowedByMatchingUnassigned
            ? ", " + UnassignedEventJson(
                new ReassignmentEventScenario { Event = ReassignmentEventShape.Unassigned },
                ReassignmentEventScenario.TrailingEventOffset)
            : string.Empty;

        return $$"""
            {
              "update": {
                "Reassignment": {
                  "value": {
                    "offset": "{{Wire(ReassignmentEventScenario.ReassignmentOffset)}}",
                    "events": [{{ReassignmentEventJson(scenario)}}{{trailing}}]
                  }
                }
              }
            }
            """;
    }

    private static string ReassignmentEventJson(ReassignmentEventScenario scenario)
    {
        var eventOffset = scenario.OmitEventOffset ? UnsetOffset : ReassignmentEventScenario.EventOffset;
        return scenario.Event switch
        {
            ReassignmentEventShape.Assigned =>
                $$"""
                {
                  "JsAssignmentEvent": {
                    {{OptionalField("source", scenario.Source)}}
                    {{OptionalField("target", scenario.Target)}}
                    "reassignmentId": "{{ReassignmentEventScenario.ReassignmentId}}",
                    "reassignmentCounter": "{{Wire(ReassignmentEventScenario.ReassignmentCounter)}}"
                    {{(scenario.OmitCreatedEvent
                        ? string.Empty
                        : ", \"createdEvent\": " + CreatedEventJson(
                            scenario.EntityName, eventOffset, scenario.OmitTemplateId))}}
                  }
                }
                """,
            ReassignmentEventShape.Unassigned => UnassignedEventJson(scenario, eventOffset),
            ReassignmentEventShape.Empty => "{}",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }

    private static string UnassignedEventJson(ReassignmentEventScenario scenario, long offset) =>
        $$"""
        {
          "JsUnassignedEvent": {
            "value": {
              {{OptionalField("source", scenario.Source)}}
              {{OptionalField("target", scenario.Target)}}
              "contractId": "{{ActiveContractScenario.ContractId}}",
              {{TemplateIdField(scenario.EntityName, scenario.OmitTemplateId)}}
              "reassignmentId": "{{ReassignmentEventScenario.ReassignmentId}}",
              "reassignmentCounter": "{{Wire(ReassignmentEventScenario.ReassignmentCounter)}}",
              "offset": "{{Wire(offset)}}",
              "witnessParties": []
            }
          }
        }
        """;

    private static string CreatedEventJson(string entityName, long offset, bool omitTemplateId) =>
        $$"""
        {
          "offset": "{{Wire(offset)}}",
          "nodeId": 0,
          "contractId": "{{ActiveContractScenario.ContractId}}",
          {{TemplateIdField(entityName, omitTemplateId)}}
          "createArgument": {{PayloadRecordJson(ActiveContractScenario.CreateArgumentValue)}},
          "witnessParties": []
        }
        """;

    private static string TemplateIdField(string entityName, bool omitTemplateId) =>
        omitTemplateId
            ? string.Empty
            : $$"""
              "templateId": {
                "packageId": "tmpl-pkg",
                "moduleName": "{{ActiveContractScenario.ModuleName}}",
                "entityName": "{{entityName}}"
              },
              """;

    private static string Wire(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static async Task<GetActiveContractsResponse> ActiveContractsResponseAsync(ActiveContractScenario scenario)
    {
        var (api, transport) = RestApiFactory.Build<IStateServiceApi>();
        transport.WithResponse(HttpStatusCode.OK, BuildResponseJson(scenario));
        return await api.GetActiveContracts(new GetActiveContractsRequest(), TestContext.Current.CancellationToken);
    }

    private static string BuildResponseJson(ActiveContractScenario scenario)
    {
        var createdEntry = scenario.OmitCreatedEvent
            ? string.Empty
            : $"\"createdEvent\": {CreatedEventJson(scenario)},";

        return scenario.Entry switch
        {
            ActiveContractEntry.Active =>
                $$"""
                {
                  "contractEntry": {
                    "JsActiveContract": {
                      {{createdEntry}}
                      {{OptionalField("synchronizerId", scenario.Synchronizer)}}
                      "reassignmentCounter": "0"
                    }
                  }
                }
                """,
            ActiveContractEntry.IncompleteUnassigned =>
                $$"""
                {
                  "contractEntry": {
                    "JsIncompleteUnassigned": {
                      {{createdEntry}}
                      "unassignedEvent": {
                        {{OptionalField("contractId", scenario.OmitUnassignedContractId ? null : ActiveContractScenario.ContractId)}}
                        {{OptionalField("source", scenario.Synchronizer)}}
                        "target": "{{ActiveContractScenario.CounterpartSynchronizerId}}",
                        "offset": "{{ActiveContractScenario.UnassignedOffset.ToString(CultureInfo.InvariantCulture)}}",
                        "reassignmentId": "{{ActiveContractScenario.ReassignmentId}}",
                        "reassignmentCounter": "{{ActiveContractScenario.ReassignmentCounter.ToString(CultureInfo.InvariantCulture)}}"
                      }
                    }
                  }
                }
                """,
            ActiveContractEntry.IncompleteAssigned =>
                $$"""
                {
                  "contractEntry": {
                    "JsIncompleteAssigned": {
                      "assignedEvent": {
                        {{createdEntry}}
                        "source": "{{ActiveContractScenario.CounterpartSynchronizerId}}",
                        {{OptionalField("target", scenario.Synchronizer)}}
                        "reassignmentCounter": "0"
                      }
                    }
                  }
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }

    private static string OptionalField(string name, string? value) =>
        value is null ? string.Empty : $"\"{name}\": \"{value}\",";

    private static string CreatedEventJson(ActiveContractScenario scenario) =>
        $$"""
        {
          "offset": "{{ActiveContractScenario.CreatedOffset.ToString(CultureInfo.InvariantCulture)}}",
          "nodeId": 0,
          "contractId": "{{ActiveContractScenario.ContractId}}",
          "templateId": {
            "packageId": "tmpl-pkg",
            "moduleName": "{{ActiveContractScenario.ModuleName}}",
            "entityName": "{{scenario.EntityName}}"
          },
          "createArgument": {{PayloadRecordJson(ActiveContractScenario.CreateArgumentValue)}},
          {{InterfaceViewsField(scenario.InterfaceView)}}
          "witnessParties": []
        }
        """;

    private static string InterfaceViewsField(InterfaceViewRendering rendering) =>
        InterfaceViewJson(rendering) is { } view ? $"\"interfaceViews\": [{view}]," : string.Empty;

    private static string? InterfaceViewJson(InterfaceViewRendering rendering) => rendering switch
    {
        InterfaceViewRendering.None => null,
        InterfaceViewRendering.Computed =>
            $$"""
            {
              "interfaceId": {{SubscribedInterfaceIdJson}},
              "viewStatus": {{ViewStatusJson(ActiveContractScenario.ComputedViewStatusCode)}},
              "viewValue": {{PayloadRecordJson(ActiveContractScenario.InterfaceViewValue)}}
            }
            """,
        InterfaceViewRendering.ComputationFailed =>
            $$"""
            {
              "interfaceId": {{SubscribedInterfaceIdJson}},
              "viewStatus": {{ViewStatusJson(ActiveContractScenario.FailedViewStatusCode)}}
            }
            """,
        InterfaceViewRendering.ValueOmitted =>
            $$"""
            {
              "interfaceId": {{SubscribedInterfaceIdJson}},
              "viewStatus": {{ViewStatusJson(ActiveContractScenario.ComputedViewStatusCode)}}
            }
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(rendering)),
    };

    private static string SubscribedInterfaceIdJson =>
        $$"""
        {
          "packageId": "{{ActiveContractScenario.InterfacePackageId}}",
          "moduleName": "{{ActiveContractScenario.InterfaceModuleName}}",
          "entityName": "{{ActiveContractScenario.InterfaceEntityName}}"
        }
        """;

    private static string ViewStatusJson(int code) =>
        $$"""
        {
          "code": {{code.ToString(CultureInfo.InvariantCulture)}},
          "message": ""
        }
        """;

    private static string PayloadRecordJson(string value) =>
        $$"""
        {
          "fields": [
            {
              "label": "{{ActiveContractScenario.OwnerFieldName}}",
              "value": {
                "party": "{{ActiveContractScenario.OwnerParty}}"
              }
            },
            {
              "label": "{{ActiveContractScenario.PayloadFieldName}}",
              "value": {
                "text": "{{value}}"
              }
            }
          ]
        }
        """;
}
