// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Canton.Ledger.Testing.Helpers;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Xunit;
using WireUpdate = Canton.Ledger.Rest.Client.Raw.GetUpdatesResponse;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class ContractStreamProjectorInterfaceViewTests
{
    private const string ImplementingTemplateIdJson =
        """{"packageId": "impl-pkg", "moduleName": "Token.Impl", "entityName": "Asset"}""";

    private const string CreateArgumentJson =
        """{"fields": [{"label": "amount", "value": {"text": "create-argument-value"}}]}""";

    private const string ComputedViewJson =
        """
        {
          "interfaceId": {"packageId": "iface-pkg", "moduleName": "Token.Api", "entityName": "IHolding"},
          "viewStatus": {"code": 0, "message": ""},
          "viewValue": {"fields": [{"label": "amount", "value": {"text": "view-value"}}]}
        }
        """;

    private const string FailedViewJson =
        """
        {
          "interfaceId": {"packageId": "iface-pkg", "moduleName": "Token.Api", "entityName": "IHolding"},
          "viewStatus": {"code": 2, "message": "view computation failed"}
        }
        """;

    private const string ViewValueOmittedJson =
        """
        {
          "interfaceId": {"packageId": "iface-pkg", "moduleName": "Token.Api", "entityName": "IHolding"},
          "viewStatus": {"code": 0, "message": ""}
        }
        """;

    public static TheoryData<string> UndecodableInterfaceViews() => new(FailedViewJson, ViewValueOmittedJson);

    private static string CreatedEventJson(
        string interfaceViewsJson,
        string templateIdJson = ImplementingTemplateIdJson,
        string offset = "42") =>
        $$"""
        {
          "offset": "{{offset}}",
          "nodeId": 0,
          "contractId": "00holding",
          "templateId": {{templateIdJson}},
          "createArgument": {{CreateArgumentJson}},
          "interfaceViews": [{{interfaceViewsJson}}],
          "witnessParties": ["alice::ns1"]
        }
        """;

    private static async Task<GetActiveContractsResponse> ActiveContractsResponseFrom(string createdEventJson)
    {
        var (api, transport) = RestApiFactory.Build<IStateServiceApi>();
        transport.WithResponse(
            HttpStatusCode.OK,
            $$"""
            {
              "contractEntry": {
                "JsActiveContract": {
                  "createdEvent": {{createdEventJson}},
                  "synchronizerId": "sync-1",
                  "reassignmentCounter": "0"
                }
              }
            }
            """);
        return await api.GetActiveContracts(new GetActiveContractsRequest(), TestContext.Current.CancellationToken);
    }

    private static Raw.Transaction TransactionFrom(string createdEventJson)
    {
        var update = JsonSerializer.Deserialize<WireUpdate>(
            $$"""
            {
              "update": {
                "Transaction": {
                  "value": {
                    "offset": "42",
                    "synchronizerId": "sync-1",
                    "events": [{"CreatedEvent": {{createdEventJson}}}]
                  }
                }
              }
            }
            """,
            RestRefitSettings.SerializerOptions);
        return update!.Update.Transaction;
    }

    private static Raw.Reassignment ReassignmentFrom(string createdEventJson, string reassignmentOffset = "42")
    {
        var update = JsonSerializer.Deserialize<WireUpdate>(
            $$"""
            {
              "update": {
                "Reassignment": {
                  "value": {
                    "offset": "{{reassignmentOffset}}",
                    "events": [
                      {
                        "JsAssignmentEvent": {
                          "source": "sync-1",
                          "target": "sync-2",
                          "reassignmentId": "reassign-1",
                          "reassignmentCounter": "3",
                          "createdEvent": {{createdEventJson}}
                        }
                      }
                    ]
                  }
                }
              }
            }
            """,
            RestRefitSettings.SerializerOptions);
        return update!.Update.Reassignment;
    }

    [Fact]
    public async Task ProjectActiveContractEntry_decodes_the_participant_computed_view_for_an_interface_marker()
    {
        var response = await ActiveContractsResponseFrom(CreatedEventJson(ComputedViewJson));

        var projected = ContractStreamProjector.ProjectActiveContractEntry<InterfaceMarker>(response)
            .Should().ContainSingle().Subject;

        var created = projected.Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Created>().Subject;
        created.ContractId.Value.Should().Be("00holding");
        created.Offset.Value.Should().Be(42L);
        created.Payload.GetRequiredField("amount").As<DamlText>().Value.Should().Be("view-value");
    }

    [Fact]
    public async Task ProjectActiveContractEntry_decodes_a_circe_shaped_view_value_through_the_daml_json_codec()
    {
        var response = await ActiveContractsResponseFrom(CreatedEventJson(
            """
            {
              "interfaceId": {"packageId": "iface-pkg", "moduleName": "Token.Api", "entityName": "IHolding"},
              "viewStatus": {"code": 0, "message": ""},
              "viewValue": {"owner": "alice::ns1", "amount": "10.5"}
            }
            """));

        var projected = ContractStreamProjector.ProjectActiveContractEntry<InterfaceMarker>(response)
            .Should().ContainSingle().Subject;

        var created = projected.Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Created>().Subject;
        created.Payload.GetRequiredField("owner").As<DamlText>().Value.Should().Be("alice::ns1");
        created.Payload.GetRequiredField("amount").As<DamlNumeric>().Value.Should().Be(10.5m);
    }

    [Theory]
    [MemberData(nameof(UndecodableInterfaceViews))]
    public async Task ProjectActiveContractEntry_yields_Unclassified_when_the_interface_view_is_undecodable(
        string undecodableViewJson)
    {
        var response = await ActiveContractsResponseFrom(CreatedEventJson(undecodableViewJson));

        var projected = ContractStreamProjector.ProjectActiveContractEntry<InterfaceMarker>(response)
            .Should().ContainSingle().Subject;

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(42L);
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_never_projects_the_implementing_templates_create_argument_onto_an_interface_row()
    {
        var response = await ActiveContractsResponseFrom(CreatedEventJson(ComputedViewJson));

        var projected = ContractStreamProjector.ProjectActiveContractEntry<InterfaceMarker>(response)
            .Should().ContainSingle().Subject;

        var created = projected.Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Created>().Subject;
        created.Payload.GetRequiredField("amount").As<DamlText>().Value
            .Should().NotBe("create-argument-value");
    }

    [Fact]
    public void ProjectTransactionEvents_decodes_the_participant_computed_view_for_an_interface_marker()
    {
        var transaction = TransactionFrom(CreatedEventJson(ComputedViewJson));

        var projected = ContractStreamProjector.ProjectTransactionEvents<InterfaceMarker>(transaction)
            .Should().ContainSingle().Subject;

        var created = projected.Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Created>().Subject;
        created.ContractId.Value.Should().Be("00holding");
        created.Payload.GetRequiredField("amount").As<DamlText>().Value.Should().Be("view-value");
    }

    [Theory]
    [MemberData(nameof(UndecodableInterfaceViews))]
    public void ProjectTransactionEvents_yields_Unclassified_when_the_interface_view_is_undecodable(
        string undecodableViewJson)
    {
        var transaction = TransactionFrom(CreatedEventJson(undecodableViewJson));

        var projected = ContractStreamProjector.ProjectTransactionEvents<InterfaceMarker>(transaction)
            .Should().ContainSingle().Subject;

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(42L);
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_still_projects_the_create_argument_for_a_template_marker()
    {
        var response = await ActiveContractsResponseFrom(CreatedEventJson(
            ComputedViewJson,
            templateIdJson: """{"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"}"""));

        var projected = ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response)
            .Should().ContainSingle().Subject;

        var created = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject;
        created.Payload.GetRequiredField("amount").As<DamlText>().Value.Should().Be("create-argument-value");
    }

    [Fact]
    public void ProjectTransactionEvents_still_projects_the_create_argument_for_a_template_marker()
    {
        var transaction = TransactionFrom(CreatedEventJson(
            ComputedViewJson,
            templateIdJson: """{"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"}"""));

        var projected = ContractStreamProjector.ProjectTransactionEvents<TemplateMarker>(transaction)
            .Should().ContainSingle().Subject;

        var created = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject;
        created.Payload.GetRequiredField("amount").As<DamlText>().Value.Should().Be("create-argument-value");
    }

    [Fact]
    public async Task ProjectActiveContractEntry_yields_Unclassified_when_no_view_matches_the_subscribed_interface()
    {
        var response = await ActiveContractsResponseFrom(CreatedEventJson(
            """
            {
              "interfaceId": {"packageId": "iface-pkg", "moduleName": "Token.Api", "entityName": "IOther"},
              "viewStatus": {"code": 0, "message": ""},
              "viewValue": {"fields": [{"label": "amount", "value": {"text": "other-view-value"}}]}
            }
            """));

        var projected = ContractStreamProjector.ProjectActiveContractEntry<InterfaceMarker>(response)
            .Should().ContainSingle().Subject;

        projected.Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Unclassified>()
            .Which.Kind.Should().Be(UnclassifiedKind.CreatedEvent);
    }

    [Fact]
    public void ProjectReassignmentEvents_Assigned_decodes_the_participant_computed_view_for_an_interface_marker()
    {
        var reassignment = ReassignmentFrom(CreatedEventJson(ComputedViewJson));

        var projected = ContractStreamProjector.ProjectReassignmentEvents<InterfaceMarker>(reassignment)
            .Should().ContainSingle().Subject;

        var assigned = projected.Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Assigned>().Subject;
        assigned.ContractId.Value.Should().Be("00holding");
        assigned.Offset.Value.Should().Be(42L);
        assigned.Payload.GetRequiredField("amount").As<DamlText>().Value.Should().Be("view-value");
    }

    [Fact]
    public void ProjectReassignmentEvents_Assigned_never_projects_the_implementing_templates_create_argument_onto_an_interface_row()
    {
        var reassignment = ReassignmentFrom(CreatedEventJson(ComputedViewJson));

        var projected = ContractStreamProjector.ProjectReassignmentEvents<InterfaceMarker>(reassignment)
            .Should().ContainSingle().Subject;

        var assigned = projected.Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Assigned>().Subject;
        assigned.Payload.GetRequiredField("amount").As<DamlText>().Value
            .Should().NotBe("create-argument-value");
    }

    [Theory]
    [MemberData(nameof(UndecodableInterfaceViews))]
    public void ProjectReassignmentEvents_Assigned_yields_Unclassified_when_the_interface_view_is_undecodable(
        string undecodableViewJson)
    {
        var reassignment = ReassignmentFrom(CreatedEventJson(undecodableViewJson));

        var projected = ContractStreamProjector.ProjectReassignmentEvents<InterfaceMarker>(reassignment)
            .Should().ContainSingle().Subject;

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(42L);
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
    }

    [Theory]
    [MemberData(nameof(UndecodableInterfaceViews))]
    public void ProjectReassignmentEvents_Assigned_reports_an_unavailable_interface_view_carrying_no_offset_at_the_reassignment_offset(
        string undecodableViewJson)
    {
        var reassignment = ReassignmentFrom(
            CreatedEventJson(undecodableViewJson, offset: "0"),
            reassignmentOffset: "77");

        var projected = ContractStreamProjector.ProjectReassignmentEvents<InterfaceMarker>(reassignment)
            .Should().ContainSingle().Subject;

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(77L);
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
    }

    [Theory]
    [MemberData(nameof(UndecodableInterfaceViews))]
    public void ProjectReassignmentEvents_Assigned_prefers_the_created_offset_over_the_reassignment_offset_for_an_unavailable_interface_view(
        string undecodableViewJson)
    {
        var reassignment = ReassignmentFrom(
            CreatedEventJson(undecodableViewJson, offset: "21"),
            reassignmentOffset: "77");

        var projected = ContractStreamProjector.ProjectReassignmentEvents<InterfaceMarker>(reassignment)
            .Should().ContainSingle().Subject;

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(21L);
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
    }

    [Fact]
    public void ProjectReassignmentEvents_Assigned_still_projects_the_create_argument_for_a_template_marker()
    {
        var reassignment = ReassignmentFrom(CreatedEventJson(
            ComputedViewJson,
            templateIdJson: """{"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"}"""));

        var projected = ContractStreamProjector.ProjectReassignmentEvents<TemplateMarker>(reassignment)
            .Should().ContainSingle().Subject;

        var assigned = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Assigned>().Subject;
        assigned.ReassignmentId.Should().Be("reassign-1");
        assigned.Payload.GetRequiredField("amount").As<DamlText>().Value.Should().Be("create-argument-value");
    }
}
