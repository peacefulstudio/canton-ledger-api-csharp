// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using Canton.Ledger.Rest.Client.Raw;
using Canton.Ledger.Testing.Helpers;
using Daml.Runtime.Streams;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestContractStreamProjectorParityTests : ContractStreamProjectorParityTests
{
    protected override async Task<IReadOnlyList<ContractStreamEvent<TemplateMarker>>> ProjectActiveContractEntryAsync(
        ActiveContractScenario scenario)
    {
        var response = await ActiveContractsResponseAsync(scenario);

        return ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response).ToList();
    }

    protected override async Task<IReadOnlyList<ContractStreamEvent<InterfaceMarker>>> ProjectActiveContractEntryAsInterfaceAsync(
        ActiveContractScenario scenario)
    {
        var response = await ActiveContractsResponseAsync(scenario);

        return ContractStreamProjector.ProjectActiveContractEntry<InterfaceMarker>(response).ToList();
    }

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
                        "contractId": "{{ActiveContractScenario.ContractId}}",
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
              "label": "{{ActiveContractScenario.PayloadFieldName}}",
              "value": {
                "text": "{{value}}"
              }
            }
          ]
        }
        """;
}
