// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Testing.Helpers;
using Canton.Ledger.Rest.Client.Raw;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class ContractStreamProjectorTests
{
    private static async Task<GetActiveContractsResponse> ActiveContractsResponseFrom(string json)
    {
        var (api, transport) = RestApiFactory.Build<IStateServiceApi>();
        transport.WithResponse(HttpStatusCode.OK, json);
        return await api.GetActiveContracts(new GetActiveContractsRequest(), TestContext.Current.CancellationToken);
    }

    private static ContractStreamEvent<TemplateMarker> ProjectSingleActiveContractEntry(
        GetActiveContractsResponse response,
        ILogger? logger = null,
        LedgerOffset? snapshotOffset = null) =>
        ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response, logger, snapshotOffset)
            .Should().ContainSingle().Subject;

    [Fact]
    public async Task ProjectActiveContractEntry_projects_an_active_contract_into_Created()
    {
        var response = await ActiveContractsResponseFrom(
            """
            {
              "contractEntry": {
                "JsActiveContract": {
                  "createdEvent": {
                    "offset": "42",
                    "nodeId": 0,
                    "contractId": "00holding",
                    "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                    "createArgument": {"fields": [{"label": "owner", "value": {"party": "alice::ns1"}}]},
                    "witnessParties": ["alice::ns1"]
                  },
                  "synchronizerId": "sync-1",
                  "reassignmentCounter": "0"
                }
              }
            }
            """);

        var projected = ProjectSingleActiveContractEntry(response);

        var created = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject;
        created.ContractId.Value.Should().Be("00holding");
        created.Offset.Value.Should().Be(42L);
        created.SynchronizerId.Should().Be(new SynchronizerId("sync-1"));
        created.WitnessParties.Should().ContainSingle().Which.Should().Be((Party)"alice::ns1");
        created.Payload.GetRequiredField("owner").As<DamlParty>().Value.Should().Be("alice::ns1");
    }

    [Fact]
    public async Task ProjectActiveContractEntry_surfaces_a_template_mismatch_as_Unclassified_created_event()
    {
        var response = await ActiveContractsResponseFrom(
            """
            {
              "contractEntry": {
                "JsActiveContract": {
                  "createdEvent": {
                    "offset": "42",
                    "contractId": "00other",
                    "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Other"},
                    "createArgument": {"fields": []}
                  },
                  "synchronizerId": "sync-1"
                }
              }
            }
            """);

        var projected = ProjectSingleActiveContractEntry(response);

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(42L);
        unclassified.Kind.Should().Be(UnclassifiedKind.CreatedEvent);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_surfaces_a_missing_synchronizer_id_as_Unclassified()
    {
        var response = await ActiveContractsResponseFrom(
            """
            {
              "contractEntry": {
                "JsActiveContract": {
                  "createdEvent": {
                    "offset": "42",
                    "contractId": "00holding",
                    "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                    "createArgument": {"fields": []}
                  }
                }
              }
            }
            """);

        var projected = ProjectSingleActiveContractEntry(response);

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(42L);
        unclassified.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"workflowId": "wf-1"}""")]
    [InlineData("""{"contractEntry": {"JsActiveContract": {"synchronizerId": "sync-1"}}}""")]
    [InlineData("""{"contractEntry": {"JsIncompleteAssigned": {"assignedEvent": {"target": "sync-2"}}}}""")]
    [InlineData("""{"contractEntry": {"JsIncompleteUnassigned": {}}}""")]
    [InlineData("""{"contractEntry": {"JsIncompleteUnassigned": {"unassignedEvent": {"source": "sync-1", "target": "sync-2"}}}}""")]
    public async Task ProjectActiveContractEntry_surfaces_an_entry_without_a_created_event_as_Unclassified(
        string json)
    {
        var response = await ActiveContractsResponseFrom(json);

        var projected = ProjectSingleActiveContractEntry(response);

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(0L);
        unclassified.Kind.Should().Be(UnclassifiedKind.Unknown);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"workflowId": "wf-1"}""")]
    [InlineData("""{"contractEntry": {"JsActiveContract": {"synchronizerId": "sync-1"}}}""")]
    [InlineData("""{"contractEntry": {"JsIncompleteAssigned": {"assignedEvent": {"target": "sync-2"}}}}""")]
    [InlineData("""{"contractEntry": {"JsIncompleteUnassigned": {}}}""")]
    [InlineData("""{"contractEntry": {"JsIncompleteUnassigned": {"unassignedEvent": {"source": "sync-1", "target": "sync-2"}}}}""")]
    public async Task ProjectActiveContractEntry_reports_an_entry_without_a_created_event_at_the_snapshot_offset(
        string json)
    {
        var response = await ActiveContractsResponseFrom(json);

        var projected = ProjectSingleActiveContractEntry(response, snapshotOffset: LedgerOffset.At(77));

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(77L);
        unclassified.Kind.Should().Be(UnclassifiedKind.Unknown);
    }

    private static Task<GetActiveContractsResponse> IncompleteUnassignedResponseFrom(
        string createdEntityName,
        string unassignedEventJson) =>
        ActiveContractsResponseFrom(
            $$"""
            {
              "contractEntry": {
                "JsIncompleteUnassigned": {
                  "createdEvent": {
                    "offset": "42",
                    "contractId": "00holding",
                    "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "{{createdEntityName}}"},
                    "createArgument": {"fields": []}
                  },
                  "unassignedEvent": {{unassignedEventJson}}
                }
              }
            }
            """);

    [Fact]
    public async Task ProjectActiveContractEntry_projects_an_incomplete_unassigned_entry_as_Created_on_the_source_then_Unassigned_source_to_target()
    {
        var response = await IncompleteUnassignedResponseFrom(
            "Holding",
            """
            {
              "contractId": "00holding",
              "source": "sync-1",
              "target": "sync-2",
              "offset": "50",
              "reassignmentId": "reassignment-1",
              "reassignmentCounter": "7",
              "witnessParties": ["alice::ns1"]
            }
            """);

        var projected = ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response).ToList();

        projected.Should().HaveCount(2);
        var created = projected[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject;
        created.SynchronizerId.Should().Be(new SynchronizerId("sync-1"));
        created.Offset.Value.Should().Be(42L);
        var unassigned = projected[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unassigned>().Subject;
        unassigned.ContractId.Value.Should().Be("00holding");
        unassigned.Offset.Value.Should().Be(50L);
        unassigned.Source.Should().Be(new SynchronizerId("sync-1"));
        unassigned.Target.Should().Be(new SynchronizerId("sync-2"));
        unassigned.ReassignmentId.Should().Be("reassignment-1");
        unassigned.ReassignmentCounter.Should().Be(7L);
        unassigned.WitnessParties.Should().ContainSingle().Which.Should().Be((Party)"alice::ns1");
    }

    [Fact]
    public async Task ProjectActiveContractEntry_surfaces_Unclassified_instead_of_the_Unassigned_when_the_unassignment_target_is_missing()
    {
        var response = await IncompleteUnassignedResponseFrom(
            "Holding",
            """{"contractId": "00holding", "source": "sync-1", "offset": "50", "reassignmentCounter": "7"}""");

        var projected = ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response).ToList();

        projected.Should().HaveCount(2);
        projected[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>();
        var unclassified = projected[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(50L);
        unclassified.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_omits_the_Unassigned_when_the_incomplete_unassigned_created_does_not_match_the_marker()
    {
        var response = await IncompleteUnassignedResponseFrom(
            "Other",
            """{"contractId": "00holding", "source": "sync-1", "target": "sync-2", "offset": "50", "reassignmentCounter": "7"}""");

        var projected = ProjectSingleActiveContractEntry(response);

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(42L);
        unclassified.Kind.Should().Be(UnclassifiedKind.CreatedEvent);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_surfaces_an_unparseable_unassignment_counter_as_Unclassified_decode_failure()
    {
        var response = await IncompleteUnassignedResponseFrom(
            "Holding",
            """{"contractId": "00holding", "source": "sync-1", "target": "sync-2", "offset": "50", "reassignmentCounter": "not-a-number"}""");
        var loggerFactory = new CapturingLoggerFactory();

        var projected = ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(
            response, loggerFactory.CreateLogger("test")).ToList();

        projected.Should().HaveCount(2);
        projected[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>();
        var unclassified = projected[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(50L);
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_surfaces_an_unassignment_without_a_contract_id_as_Unclassified_decode_failure()
    {
        var response = await IncompleteUnassignedResponseFrom(
            "Holding",
            """{"source": "sync-1", "target": "sync-2", "offset": "50", "reassignmentCounter": "7"}""");
        var loggerFactory = new CapturingLoggerFactory();

        var projected = ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(
            response, loggerFactory.CreateLogger("test")).ToList();

        projected.Should().HaveCount(2);
        projected[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>();
        var unclassified = projected[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(50L);
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_surfaces_an_incomplete_unassigned_entry_without_a_created_event_at_the_unassignment_offset()
    {
        var response = await ActiveContractsResponseFrom(
            """{"contractEntry": {"JsIncompleteUnassigned": {"unassignedEvent": {"source": "sync-1", "target": "sync-2", "offset": "50"}}}}""");

        var projected = ProjectSingleActiveContractEntry(response, snapshotOffset: LedgerOffset.At(77));

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(50L);
        unclassified.Kind.Should().Be(UnclassifiedKind.Unknown);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_projects_an_incomplete_assigned_entry_using_the_target_synchronizer()
    {
        var response = await ActiveContractsResponseFrom(
            """
            {
              "contractEntry": {
                "JsIncompleteAssigned": {
                  "assignedEvent": {
                    "source": "sync-1",
                    "target": "sync-2",
                    "createdEvent": {
                      "offset": "43",
                      "contractId": "00holding",
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                      "createArgument": {"fields": []}
                    }
                  }
                }
              }
            }
            """);

        var projected = ProjectSingleActiveContractEntry(response);

        var created = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject;
        created.SynchronizerId.Should().Be(new SynchronizerId("sync-2"));
        created.Offset.Value.Should().Be(43L);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_surfaces_undecodable_create_arguments_as_Unclassified_decode_failure_and_logs_a_warning()
    {
        var response = await ActiveContractsResponseFrom(
            """
            {
              "contractEntry": {
                "JsActiveContract": {
                  "createdEvent": {
                    "offset": "42",
                    "contractId": "00holding",
                    "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                    "createArgument": {"fields": [{"label": "owner", "value": {"int64": "not-a-number"}}]}
                  },
                  "synchronizerId": "sync-1"
                }
              }
            }
            """);
        var loggerFactory = new CapturingLoggerFactory();

        var projected = ProjectSingleActiveContractEntry(
            response, loggerFactory.CreateLogger("test"));

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(42L);
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_surfaces_an_unparseable_created_offset_as_Unclassified_decode_failure()
    {
        var response = await ActiveContractsResponseFrom(
            """
            {
              "contractEntry": {
                "JsActiveContract": {
                  "createdEvent": {
                    "offset": "not-a-number",
                    "contractId": "00other",
                    "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Other"},
                    "createArgument": {"fields": []}
                  },
                  "synchronizerId": "sync-1"
                }
              }
            }
            """);
        var loggerFactory = new CapturingLoggerFactory();

        var projected = ProjectSingleActiveContractEntry(
            response, loggerFactory.CreateLogger("test"));

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(
            record => record.Level == LogLevel.Warning && record.Message.Contains("not-a-number"));
    }

    [Fact]
    public async Task ProjectActiveContractEntry_reports_an_unparseable_created_offset_at_the_snapshot_offset()
    {
        var response = await ActiveContractsResponseFrom(
            """
            {
              "contractEntry": {
                "JsActiveContract": {
                  "createdEvent": {
                    "offset": "not-a-number",
                    "contractId": "00other",
                    "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Other"},
                    "createArgument": {"fields": []}
                  },
                  "synchronizerId": "sync-1"
                }
              }
            }
            """);
        var loggerFactory = new CapturingLoggerFactory();

        var projected = ProjectSingleActiveContractEntry(
            response, loggerFactory.CreateLogger("test"), LedgerOffset.At(77));

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(77L);
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(
            record => record.Level == LogLevel.Warning
                && record.Message.Contains("not-a-number")
                && record.Message.Contains("snapshot offset 77"));
    }

    [Fact]
    public async Task ProjectActiveContractEntry_surfaces_an_unparseable_unassignment_offset_as_Unclassified_decode_failure()
    {
        var response = await ActiveContractsResponseFrom(
            """{"contractEntry": {"JsIncompleteUnassigned": {"unassignedEvent": {"source": "sync-1", "target": "sync-2", "offset": "not-a-number"}}}}""");
        var loggerFactory = new CapturingLoggerFactory();

        var projected = ProjectSingleActiveContractEntry(
            response, loggerFactory.CreateLogger("test"));

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(
            record => record.Level == LogLevel.Warning && record.Message.Contains("not-a-number"));
    }

    [Fact]
    public async Task ProjectActiveContractEntry_reports_an_unparseable_unassignment_offset_at_the_snapshot_offset()
    {
        var response = await ActiveContractsResponseFrom(
            """{"contractEntry": {"JsIncompleteUnassigned": {"unassignedEvent": {"source": "sync-1", "target": "sync-2", "offset": "not-a-number"}}}}""");
        var loggerFactory = new CapturingLoggerFactory();

        var projected = ProjectSingleActiveContractEntry(
            response, loggerFactory.CreateLogger("test"), LedgerOffset.At(77));

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(77L);
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(
            record => record.Level == LogLevel.Warning
                && record.Message.Contains("not-a-number")
                && record.Message.Contains("snapshot offset 77"));
    }

    private static async Task<GetActiveContractsResponse> ActiveContractsResponseWithArguments(string createArgumentJson) =>
        await ActiveContractsResponseFrom(
            $$"""
            {
              "contractEntry": {
                "JsActiveContract": {
                  "createdEvent": {
                    "offset": "42",
                    "contractId": "00holding",
                    "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                    "createArgument": {{createArgumentJson}}
                  },
                  "synchronizerId": "sync-1"
                }
              }
            }
            """);

    [Theory]
    [MemberData(nameof(WireValueCases))]
    public async Task ProjectActiveContractEntry_decodes_each_wire_Value_sum_case(string valueJson, DamlValue expected)
    {
        var response = await ActiveContractsResponseWithArguments(
            $$"""{"fields": [{"label": "v", "value": {{valueJson}}}]}""");

        var projected = ProjectSingleActiveContractEntry(response);

        var created = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject;
        created.Payload.GetRequiredField("v").Should().BeEquivalentTo(
            expected, options => options.PreferringRuntimeMemberTypes());
    }

    public static IEnumerable<object[]> WireValueCases()
    {
        yield return ["""{"bool": true}""", new DamlBool(true)];
        yield return ["""{"bool": false}""", new DamlBool(false)];
        yield return ["""{"int64": "5"}""", new DamlInt64(5)];
        yield return ["""{"date": 20000}""", DamlDate.FromDaysSinceEpoch(20000)];
        yield return ["""{"date": 0}""", DamlDate.FromDaysSinceEpoch(0)];
        yield return ["""{"timestamp": "1720000000000000"}""", DamlTimestamp.FromMicrosecondsSinceEpoch(1720000000000000)];
        yield return ["""{"numeric": "1.5"}""", new DamlNumeric(1.5m)];
        yield return ["""{"party": "alice::ns1"}""", new DamlParty("alice::ns1")];
        yield return ["""{"text": "hello"}""", new DamlText("hello")];
        yield return ["""{"contractId": "00cid"}""", new DamlContractId("00cid")];
        yield return ["""{"unit": {}}""", DamlUnit.Instance];
        yield return ["""{"optional": {}}""", new DamlOptional(null)];
        yield return ["""{"optional": {"value": {"text": "some"}}}""", new DamlOptional(new DamlText("some"))];
        yield return
        [
            """{"list": {"elements": [{"int64": "1"}, {"int64": "2"}]}}""",
            new DamlList([new DamlInt64(1), new DamlInt64(2)]),
        ];
        yield return
        [
            """{"textMap": {"entries": [{"key": "k", "value": {"int64": "1"}}]}}""",
            new DamlTextMap(new Dictionary<string, DamlValue> { ["k"] = new DamlInt64(1) }),
        ];
        yield return
        [
            """{"genMap": {"entries": [{"key": {"text": "k"}, "value": {"int64": "1"}}]}}""",
            new DamlGenMap([(new DamlText("k"), new DamlInt64(1))]),
        ];
        yield return
        [
            """{"variant": {"constructor": "Tag", "value": {"int64": "1"}}}""",
            new DamlVariant(null, "Tag", new DamlInt64(1)),
        ];
        yield return ["""{"enum": {"constructor": "Red"}}""", new DamlEnum(null, "Red")];
        yield return
        [
            """{"record": {"fields": [{"label": "n", "value": {"int64": "7"}}]}}""",
            new DamlRecord(null, [new DamlField("n", new DamlInt64(7))]),
        ];
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"int64": "not-a-number"}""")]
    [InlineData("""{"numeric": "not-a-number"}""")]
    [InlineData("""{"timestamp": "not-a-number"}""")]
    public async Task ProjectActiveContractEntry_surfaces_a_Value_whose_sum_case_is_unset_or_malformed_as_decode_failure(
        string valueJson)
    {
        var response = await ActiveContractsResponseWithArguments(
            $$"""{"fields": [{"label": "v", "value": {{valueJson}}}]}""");

        var projected = ProjectSingleActiveContractEntry(response);

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_decodes_circe_shaped_create_arguments_through_the_daml_json_codec()
    {
        var response = await ActiveContractsResponseWithArguments(
            """{"owner": "alice::ns1", "amount": "10.5"}""");

        var projected = ProjectSingleActiveContractEntry(response);

        var created = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject;
        created.Payload.GetRequiredField("owner").As<DamlText>().Value.Should().Be("alice::ns1");
        created.Payload.GetRequiredField("amount").As<DamlNumeric>().Value.Should().Be(10.5m);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("null")]
    public async Task ProjectActiveContractEntry_projects_absent_or_empty_create_arguments_as_an_empty_record(
        string createArgumentJson)
    {
        var response = await ActiveContractsResponseWithArguments(createArgumentJson);

        var projected = ProjectSingleActiveContractEntry(response);

        var created = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject;
        created.Payload.Fields.Should().BeEmpty();
    }
}
