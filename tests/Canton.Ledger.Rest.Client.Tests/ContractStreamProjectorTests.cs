// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Rest;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public class ContractStreamProjectorTests
{
    private static async Task<GetActiveContractsResponse> ActiveContractsResponseFrom(string json)
    {
        var (api, transport) = RestApiFactory.Build<IStateServiceApi>();
        transport.WithResponse(HttpStatusCode.OK, json);
        return await api.GetActiveContracts(new GetActiveContractsRequest(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_projects_an_active_contract_into_Created()
    {
        var response = await ActiveContractsResponseFrom(
            """
            {
              "active_contract": {
                "created_event": {
                  "offset": "42",
                  "node_id": 0,
                  "contract_id": "00holding",
                  "template_id": {"package_id": "tmpl-pkg", "module_name": "Sample.Token", "entity_name": "Holding"},
                  "create_arguments": {"fields": [{"label": "owner", "value": {"party": "alice::ns1"}}]},
                  "witness_parties": ["alice::ns1"]
                },
                "synchronizer_id": "sync-1",
                "reassignment_counter": "0"
              }
            }
            """);

        var projected = ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response);

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
              "active_contract": {
                "created_event": {
                  "offset": "42",
                  "contract_id": "00other",
                  "template_id": {"package_id": "tmpl-pkg", "module_name": "Sample.Token", "entity_name": "Other"},
                  "create_arguments": {"fields": []}
                },
                "synchronizer_id": "sync-1"
              }
            }
            """);

        var projected = ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response);

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
              "active_contract": {
                "created_event": {
                  "offset": "42",
                  "contract_id": "00holding",
                  "template_id": {"package_id": "tmpl-pkg", "module_name": "Sample.Token", "entity_name": "Holding"},
                  "create_arguments": {"fields": []}
                }
              }
            }
            """);

        var projected = ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response);

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(42L);
        unclassified.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"workflow_id": "wf-1"}""")]
    [InlineData("""{"active_contract": {"synchronizer_id": "sync-1"}}""")]
    [InlineData("""{"incomplete_assigned": {"assigned_event": {"target": "sync-2"}}}""")]
    public async Task ProjectActiveContractEntry_surfaces_an_entry_without_a_created_event_as_Unclassified(
        string json)
    {
        var response = await ActiveContractsResponseFrom(json);

        var projected = ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response);

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(0L);
        unclassified.Kind.Should().Be(UnclassifiedKind.Unknown);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_projects_an_incomplete_unassigned_entry_using_the_source_synchronizer()
    {
        var response = await ActiveContractsResponseFrom(
            """
            {
              "incomplete_unassigned": {
                "created_event": {
                  "offset": "42",
                  "contract_id": "00holding",
                  "template_id": {"package_id": "tmpl-pkg", "module_name": "Sample.Token", "entity_name": "Holding"},
                  "create_arguments": {"fields": []}
                },
                "unassigned_event": {"source": "sync-1", "target": "sync-2", "offset": "50"}
              }
            }
            """);

        var projected = ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response);

        var created = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject;
        created.SynchronizerId.Should().Be(new SynchronizerId("sync-1"));
        created.Offset.Value.Should().Be(42L);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_surfaces_an_incomplete_unassigned_entry_without_a_created_event_at_the_unassignment_offset()
    {
        var response = await ActiveContractsResponseFrom(
            """{"incomplete_unassigned": {"unassigned_event": {"source": "sync-1", "target": "sync-2", "offset": "50"}}}""");

        var projected = ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response);

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
              "incomplete_assigned": {
                "assigned_event": {
                  "source": "sync-1",
                  "target": "sync-2",
                  "created_event": {
                    "offset": "43",
                    "contract_id": "00holding",
                    "template_id": {"package_id": "tmpl-pkg", "module_name": "Sample.Token", "entity_name": "Holding"},
                    "create_arguments": {"fields": []}
                  }
                }
              }
            }
            """);

        var projected = ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response);

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
              "active_contract": {
                "created_event": {
                  "offset": "42",
                  "contract_id": "00holding",
                  "template_id": {"package_id": "tmpl-pkg", "module_name": "Sample.Token", "entity_name": "Holding"},
                  "create_arguments": {"fields": [{"label": "owner", "value": {"int64": "not-a-number"}}]}
                },
                "synchronizer_id": "sync-1"
              }
            }
            """);
        var loggerFactory = new CapturingLoggerFactory();

        var projected = ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(
            response, loggerFactory.CreateLogger("test"));

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(42L);
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning);
    }

    private static async Task<GetActiveContractsResponse> ActiveContractsResponseWithArguments(string createArgumentsJson) =>
        await ActiveContractsResponseFrom(
            $$"""
            {
              "active_contract": {
                "created_event": {
                  "offset": "42",
                  "contract_id": "00holding",
                  "template_id": {"package_id": "tmpl-pkg", "module_name": "Sample.Token", "entity_name": "Holding"},
                  "create_arguments": {{createArgumentsJson}}
                },
                "synchronizer_id": "sync-1"
              }
            }
            """);

    [Theory]
    [MemberData(nameof(WireValueCases))]
    public async Task ProjectActiveContractEntry_decodes_each_wire_Value_sum_case(string valueJson, DamlValue expected)
    {
        var response = await ActiveContractsResponseWithArguments(
            $$"""{"fields": [{"label": "v", "value": {{valueJson}}}]}""");

        var projected = ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response);

        var created = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject;
        created.Payload.GetRequiredField("v").Should().BeEquivalentTo(
            expected, options => options.PreferringRuntimeMemberTypes());
    }

    public static IEnumerable<object[]> WireValueCases()
    {
        yield return ["""{"bool": true}""", new DamlBool(true)];
        yield return ["""{"int64": "5"}""", new DamlInt64(5)];
        yield return ["""{"date": 20000}""", DamlDate.FromDaysSinceEpoch(20000)];
        yield return ["""{"timestamp": "1720000000000000"}""", DamlTimestamp.FromMicrosecondsSinceEpoch(1720000000000000)];
        yield return ["""{"numeric": "1.5"}""", new DamlNumeric(1.5m)];
        yield return ["""{"party": "alice::ns1"}""", new DamlParty("alice::ns1")];
        yield return ["""{"text": "hello"}""", new DamlText("hello")];
        yield return ["""{"contract_id": "00cid"}""", new DamlContractId("00cid")];
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
            """{"text_map": {"entries": [{"key": "k", "value": {"int64": "1"}}]}}""",
            new DamlTextMap(new Dictionary<string, DamlValue> { ["k"] = new DamlInt64(1) }),
        ];
        yield return
        [
            """{"gen_map": {"entries": [{"key": {"text": "k"}, "value": {"int64": "1"}}]}}""",
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
    [InlineData("""{"bool": false}""")]
    [InlineData("""{"date": 0}""")]
    [InlineData("""{"int64": "not-a-number"}""")]
    [InlineData("""{"numeric": "not-a-number"}""")]
    [InlineData("""{"timestamp": "not-a-number"}""")]
    public async Task ProjectActiveContractEntry_surfaces_a_Value_whose_sum_case_is_ambiguous_or_malformed_as_decode_failure(
        string valueJson)
    {
        var response = await ActiveContractsResponseWithArguments(
            $$"""{"fields": [{"label": "v", "value": {{valueJson}}}]}""");

        var projected = ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response);

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_decodes_circe_shaped_create_arguments_through_the_daml_json_codec()
    {
        var response = await ActiveContractsResponseWithArguments(
            """{"owner": "alice::ns1", "amount": "10.5"}""");

        var projected = ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response);

        var created = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject;
        created.Payload.GetRequiredField("owner").As<DamlText>().Value.Should().Be("alice::ns1");
        created.Payload.GetRequiredField("amount").As<DamlNumeric>().Value.Should().Be(10.5m);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("null")]
    public async Task ProjectActiveContractEntry_projects_absent_or_empty_create_arguments_as_an_empty_record(
        string createArgumentsJson)
    {
        var response = await ActiveContractsResponseWithArguments(createArgumentsJson);

        var projected = ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response);

        var created = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject;
        created.Payload.Fields.Should().BeEmpty();
    }

    internal sealed record TemplateMarker(string Owner) : ITemplate
    {
        public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "Holding");
        public static string PackageId => "tmpl-pkg";
        public static string PackageName => "token-impl";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", new DamlParty(Owner)));
    }
}
