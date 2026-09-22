// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Serialization;
using System.Text.Json;
using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
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

public class RestContractStreamProjectorTests
{

    private sealed record TemplateMarker(DamlRecord Record) : ITemplate, IDamlRecord<TemplateMarker>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "StreamHolding");

        public static string PackageId => "tmpl-pkg";

        public static string PackageName => "token-impl";

        public static Version PackageVersion { get; } = new(0, 1, 0);

        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => Record;

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context);
        public static TemplateMarker FromRecord(DamlRecord record) => new(record);
    }

    private sealed record KeylessMarker(DamlRecord Record) : ITemplate, IDamlRecord<KeylessMarker>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "Keyless");

        public static string PackageId => "tmpl-pkg";

        public static string PackageName => "token-impl";

        public static Version PackageVersion { get; } = new(0, 1, 0);

        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => Record;

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context);
        public static KeylessMarker FromRecord(DamlRecord record) => new(record);
    }

    private sealed record CirceMarker(
        [property: DamlFieldAttribute("owner")] Party Owner,
        [property: DamlFieldAttribute("amount")] decimal Amount) : ITemplate, IDamlRecord<CirceMarker>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "CirceStreamHolding");

        public static string PackageId => "tmpl-pkg";

        public static string PackageName => "token-impl";

        public static Version PackageVersion { get; } = new(0, 1, 0);

        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", Owner.ToDamlValue()),
            DamlField.Create("amount", new DamlNumeric(Amount)));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(
                json,
                context,
                ("owner", DamlLfJsonDecoders.ReadParty),
                ("amount", DamlLfJsonDecoders.ReadNumeric));
        public static CirceMarker FromRecord(DamlRecord record) => new(
            Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
            record.GetRequiredField("amount").As<DamlNumeric>().Value);
    }
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
        RestContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response, logger, snapshotOffset)
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
                    "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "CirceStreamHolding"},
                    "createArgument": {"owner": "alice::ns1", "amount": "10.5"},
                    "witnessParties": ["alice::ns1"]
                  },
                  "synchronizerId": "sync-1",
                  "reassignmentCounter": "0"
                }
              }
            }
            """);

        var projected = RestContractStreamProjector.ProjectActiveContractEntry<CirceMarker>(response)
            .Should().ContainSingle().Subject;

        var created = projected.Should().BeOfType<ContractStreamEvent<CirceMarker>.Created>().Subject;
        created.ContractId.Value.Should().Be("00holding");
        created.Offset.Value.Should().Be(42L);
        created.SynchronizerId.Should().Be(new SynchronizerId("sync-1"));
        created.WitnessParties.Should().ContainSingle().Which.Should().Be((Party)"alice::ns1");
        created.Payload.Owner.Should().Be((Party)"alice::ns1");
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
                    "createArgument": {}
                  },
                  "synchronizerId": "sync-1"
                }
              }
            }
            """);

        var projected = ProjectSingleActiveContractEntry(response);

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(42L));
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
                    "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "StreamHolding"},
                    "createArgument": {}
                  }
                }
              }
            }
            """);

        var projected = ProjectSingleActiveContractEntry(response);

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(42L));
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
        unclassified.Offset.Should().Be(LedgerOffset.At(0L));
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
        unclassified.Offset.Should().Be(LedgerOffset.At(77L));
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
                    "createArgument": {}
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
            "StreamHolding",
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

        var projected = RestContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response).ToList();

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
            "StreamHolding",
            """{"contractId": "00holding", "source": "sync-1", "offset": "50", "reassignmentCounter": "7"}""");

        var projected = RestContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response).ToList();

        projected.Should().HaveCount(2);
        projected[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>();
        var unclassified = projected[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(50L));
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
        unclassified.Offset.Should().Be(LedgerOffset.At(42L));
        unclassified.Kind.Should().Be(UnclassifiedKind.CreatedEvent);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_surfaces_an_unparseable_unassignment_counter_as_Unclassified_decode_failure()
    {
        var response = await IncompleteUnassignedResponseFrom(
            "StreamHolding",
            """{"contractId": "00holding", "source": "sync-1", "target": "sync-2", "offset": "50", "reassignmentCounter": "not-a-number"}""");
        var loggerFactory = new CapturingLoggerFactory();

        var projected = RestContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(
            response, loggerFactory.CreateLogger("test")).ToList();

        projected.Should().HaveCount(2);
        projected[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>();
        var unclassified = projected[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(50L));
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_surfaces_an_unassignment_without_a_contract_id_as_Unclassified_decode_failure()
    {
        var response = await IncompleteUnassignedResponseFrom(
            "StreamHolding",
            """{"source": "sync-1", "target": "sync-2", "offset": "50", "reassignmentCounter": "7"}""");
        var loggerFactory = new CapturingLoggerFactory();

        var projected = RestContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(
            response, loggerFactory.CreateLogger("test")).ToList();

        projected.Should().HaveCount(2);
        projected[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>();
        var unclassified = projected[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(50L));
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
        unclassified.Offset.Should().Be(LedgerOffset.At(50L));
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
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "StreamHolding"},
                      "createArgument": {}
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
        var response = await ActiveContractsResponseWithArguments(
            "CirceStreamHolding",
            """{"owner": "alice::ns1", "amount": "not-a-number"}""");
        var loggerFactory = new CapturingLoggerFactory();

        var projected = RestContractStreamProjector.ProjectActiveContractEntry<CirceMarker>(
            response, loggerFactory.CreateLogger("test")).Should().ContainSingle().Subject;

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<CirceMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(42L));
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
                    "createArgument": {}
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
        unclassified.Offset.Should().BeNull(
            "with no snapshot offset to fall back on there is no resume point to report");
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
                    "createArgument": {}
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
        unclassified.Offset.Should().Be(LedgerOffset.At(77L));
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
        unclassified.Offset.Should().BeNull(
            "with no snapshot offset to fall back on there is no resume point to report");
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
        unclassified.Offset.Should().Be(LedgerOffset.At(77L));
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(
            record => record.Level == LogLevel.Warning
                && record.Message.Contains("not-a-number")
                && record.Message.Contains("snapshot offset 77"));
    }

    private static async Task<GetActiveContractsResponse> ActiveContractsResponseWithArguments(string entityName, string createArgumentJson) =>
        await ActiveContractsResponseFrom(
            $$"""
            {
              "contractEntry": {
                "JsActiveContract": {
                  "createdEvent": {
                    "offset": "42",
                    "contractId": "00holding",
                    "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "{{entityName}}"},
                    "createArgument": {{createArgumentJson}}
                  },
                  "synchronizerId": "sync-1"
                }
              }
            }
            """);

    private static async Task<GetActiveContractsResponse> KeylessContractResponseWithContractKey(string contractKeyJson) =>
        await ActiveContractsResponseFrom(
            $$"""
            {
              "contractEntry": {
                "JsActiveContract": {
                  "createdEvent": {
                    "offset": "42",
                    "contractId": "00keyless",
                    "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Keyless"},
                    "createArgument": {},
                    "contractKey": {{contractKeyJson}}
                  },
                  "synchronizerId": "sync-1"
                }
              }
            }
            """);

    [Fact]
    public async Task ProjectActiveContractEntry_surfaces_a_contract_key_on_a_template_whose_loaded_code_declares_no_key_as_Unclassified_decode_failure()
    {
        var response = await KeylessContractResponseWithContractKey("\"alice::ns1\"");
        var loggerFactory = new CapturingLoggerFactory();

        var projected = RestContractStreamProjector.ProjectActiveContractEntry<KeylessMarker>(
            response, loggerFactory.CreateLogger("test")).Should().ContainSingle().Subject;

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<KeylessMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(42L));
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        var refusal = loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning)
            .Which.Exception.Should().BeOfType<TemplateTypeRequiredException>().Subject;
        refusal.TypeId.Should().Be("tmpl-pkg:Sample.Token:Keyless");
        refusal.Message.Should().Be(
            "A contract key arrived for 'tmpl-pkg:Sample.Token:Keyless', but the loaded generated template Canton.Ledger.Rest.Client.Tests.RestContractStreamProjectorTests+KeylessMarker declares no contract key, so it was likely generated for a different version of the package; load exactly one assembly generated for its Daml package before reading this payload over the JSON Ledger API.");
    }

    [Theory]
    [InlineData("""{"owner": "alice::ns1", "amount": "not-a-number"}""")]
    [InlineData("""{"owner": "alice::ns1", "amount": true}""")]
    [InlineData("""{"owner": 5, "amount": "10.5"}""")]
    [InlineData("""{"owner": {"party": "alice::ns1"}, "amount": "10.5"}""")]
    public async Task ProjectActiveContractEntry_surfaces_a_create_argument_that_does_not_fit_the_template_type_as_decode_failure(
        string createArgumentJson)
    {
        var response = await ActiveContractsResponseWithArguments("CirceStreamHolding", createArgumentJson);

        var projected = RestContractStreamProjector.ProjectActiveContractEntry<CirceMarker>(response)
            .Should().ContainSingle().Subject;

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<CirceMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_decodes_circe_shaped_create_arguments_against_the_template_type()
    {
        var response = await ActiveContractsResponseWithArguments(
            "CirceStreamHolding",
            """{"owner": "alice::ns1", "amount": "10.5"}""");

        var projected = RestContractStreamProjector.ProjectActiveContractEntry<CirceMarker>(response)
            .Should().ContainSingle().Subject;

        var created = projected.Should().BeOfType<ContractStreamEvent<CirceMarker>.Created>().Subject;
        created.Payload.Owner.Should().Be((Party)"alice::ns1");
        created.Payload.Amount.Should().Be(10.5m);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_projects_empty_create_arguments_as_an_empty_record()
    {
        var response = await ActiveContractsResponseWithArguments("StreamHolding", "{}");

        var projected = ProjectSingleActiveContractEntry(response);

        var created = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject;
        created.Payload.Record.Fields.Should().BeEmpty();
    }

    [Fact]
    public async Task ProjectActiveContractEntry_surfaces_a_null_create_argument_as_Unclassified_decode_failure()
    {
        var response = await ActiveContractsResponseWithArguments("StreamHolding", "null");
        var loggerFactory = new CapturingLoggerFactory();

        var projected = ProjectSingleActiveContractEntry(response, loggerFactory.CreateLogger("test"));

        projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>()
            .Which.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning)
            .Which.Exception.Should().BeOfType<MalformedResponseException>()
            .Which.Message.Should().Be(
                "Malformed response from ledger: CreatedEvent for contract '00holding' has no createArgument, though the Ledger API marks the field as required.");
    }
}
