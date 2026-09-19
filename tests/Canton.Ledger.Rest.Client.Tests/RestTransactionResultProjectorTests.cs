// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using WireTransaction = Canton.Ledger.Rest.Client.Raw.SubmitAndWaitForTransactionResponse;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestTransactionResultProjectorTests
{
    private sealed record TemplateMarker : ITemplate, IDamlRecord<TemplateMarker>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "Holding");
        public static string PackageId => "tmpl-pkg";
        public static string PackageName => "token-impl";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, []);

        public static TemplateMarker FromRecord(DamlRecord record) =>
            new();
    }

    private sealed record ScalarKeyedMarker : ITemplate, IDamlRecord<ScalarKeyedMarker>, IHasKey<ScalarKeyedMarker, Party>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "Holding");
        public static string PackageId => "tmpl-pkg";
        public static string PackageName => "token-impl";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, []);

        public static ScalarKeyedMarker FromRecord(DamlRecord record) => new();

        public static KeyDescriptor<ScalarKeyedMarker, Party> Key { get; } = new()
        {
            KeyEncoder = owner => owner.ToDamlValue(),
            KeyDecoder = value => Party.FromDamlValue(value.As<DamlParty>()),
        };
    }


    private static Raw.Transaction TransactionFrom(string json)
    {
        var response = JsonSerializer.Deserialize<WireTransaction>(json, RestRefitSettings.SerializerOptions);
        return response!.Transaction;
    }

    [Fact]
    public void Project_projects_update_id_offset_and_command_id()
    {
        var transaction = TransactionFrom(
            """
            {
              "transaction": {
                "updateId": "upd-1",
                "commandId": "cmd-1",
                "offset": "42",
                "events": []
              }
            }
            """);

        var result = RestTransactionResultProjector.Project(transaction);

        result.UpdateId.Should().Be("upd-1");
        result.CommandId.Should().Be(new CommandId("cmd-1"));
        result.CompletionOffset.Value.Should().Be(42L);
    }

    [Fact]
    public void Project_collects_created_contracts_with_decoded_payload_and_interface_ids()
    {
        var transaction = TransactionFrom(
            """
            {
              "transaction": {
                "updateId": "upd-1",
                "offset": "1",
                "events": [
                  {
                    "CreatedEvent": {
                      "offset": "1",
                      "contractId": "00holding",
                      "nodeId": 0,
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                      "createArgument": {"fields": [{"label": "owner", "value": {"party": "alice::ns1"}}]},
                      "interfaceViews": [
                        {"interfaceId": {"packageId": "iface-pkg", "moduleName": "Sample.Token", "entityName": "IHolding"}}
                      ]
                    }
                  }
                ]
              }
            }
            """);

        var result = RestTransactionResultProjector.Project(transaction);

        var created = result.CreatedContracts.Should().ContainSingle().Subject;
        created.ContractId.Should().Be("00holding");
        created.TemplateId.Should().Be(new RuntimeIdentifier("tmpl-pkg", "Sample.Token", "Holding"));
        created.Payload.GetRequiredField("owner").As<DamlParty>().Value.Should().Be("alice::ns1");
        created.InterfaceIds.Should().ContainSingle()
            .Which.Should().Be(new RuntimeIdentifier("iface-pkg", "Sample.Token", "IHolding"));
    }

    [Fact]
    public void Project_leaves_the_projected_key_hash_null_when_the_wire_carries_an_empty_contract_key_hash()
    {
        var transaction = TransactionFrom(
            """
            {
              "transaction": {
                "updateId": "upd-1",
                "offset": "1",
                "events": [
                  {
                    "CreatedEvent": {
                      "offset": "1",
                      "contractId": "00keyedWithAnEmptyHash",
                      "nodeId": 0,
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                      "createArgument": {"fields": [{"label": "owner", "value": {"party": "alice::ns1"}}]},
                      "contractKey": {"party": "alice::ns1"},
                      "contractKeyHash": ""
                    }
                  }
                ]
              }
            }
            """);

        var result = RestTransactionResultProjector.Project(transaction);

        var created = result.CreatedContracts.Should().ContainSingle().Subject;
        created.ContractKey.Should().NotBeNull();
        created.ContractKey!.Value.Should().Be(new DamlParty("alice::ns1"));
        created.ContractKey.KeyHash.Should().BeNull();
    }

    [Fact]
    public void Project_leaves_the_projected_key_hash_null_when_the_wire_carries_no_contract_key_hash()
    {
        var transaction = TransactionFrom(
            """
            {
              "transaction": {
                "updateId": "upd-1",
                "offset": "1",
                "events": [
                  {
                    "CreatedEvent": {
                      "offset": "1",
                      "contractId": "00keyedWithoutHash",
                      "nodeId": 0,
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                      "createArgument": {"fields": [{"label": "owner", "value": {"party": "alice::ns1"}}]},
                      "contractKey": {"party": "alice::ns1"}
                    }
                  }
                ]
              }
            }
            """);

        var result = RestTransactionResultProjector.Project(transaction);

        var created = result.CreatedContracts.Should().ContainSingle().Subject;
        created.ContractKey.Should().NotBeNull();
        created.ContractKey!.KeyHash.Should().BeNull();
    }

    [Fact]
    public void Project_collects_archived_contract_ids()
    {
        var transaction = TransactionFrom(
            """
            {
              "transaction": {
                "updateId": "upd-1",
                "offset": "1",
                "events": [{"ArchivedEvent": {"offset": "1", "contractId": "00archived"}}]
              }
            }
            """);

        var result = RestTransactionResultProjector.Project(transaction);

        result.ArchivedContractIds.Should().ContainSingle().Which.Should().Be("00archived");
    }

    [Fact]
    public void Project_pins_the_cross_transport_malformed_response_wording()
    {
        var transaction = TransactionFrom(
            """
            {
              "transaction": {
                "updateId": "upd-1",
                "offset": "1",
                "events": [{"CreatedEvent": {"offset": "1", "contractId": "00noTemplateId", "nodeId": 0}}]
              }
            }
            """);

        var act = () => RestTransactionResultProjector.Project(transaction);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Be(
                "Malformed response from ledger: CreatedEvent for contract '00noTemplateId' has no templateId, "
                + "though the Ledger API marks the field as required.");
    }

    [Fact]
    public void Project_refuses_a_created_event_that_carries_no_nodeId()
    {
        var transaction = TransactionFrom(
            """
            {
              "transaction": {
                "updateId": "upd-1",
                "offset": "1",
                "events": [
                  {
                    "CreatedEvent": {
                      "offset": "1",
                      "contractId": "00noNodeId",
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                      "createArgument": {"fields": []}
                    }
                  }
                ]
              }
            }
            """);

        var act = () => RestTransactionResultProjector.Project(transaction);

        act.Should().Throw<MalformedResponseException>()
            .Which.Message.Should().Be(
                "Malformed response from ledger: CreatedEvent for contract '00noNodeId' has no nodeId, "
                + "though the Ledger API marks the field as required.");
    }

    [Fact]
    public void Project_collects_exercised_events_with_decoded_choice_argument_and_result()
    {
        var transaction = TransactionFrom(
            """
            {
              "transaction": {
                "updateId": "upd-1",
                "offset": "1",
                "events": [
                  {
                    "ExercisedEvent": {
                      "offset": "1",
                      "contractId": "00holding",
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                      "choice": "Archive",
                      "choiceArgument": {"record": {"fields": []}},
                      "actingParties": ["alice::ns1"],
                      "consuming": true,
                      "witnessParties": ["alice::ns1"],
                      "exerciseResult": {"unit": {}}
                    }
                  }
                ]
              }
            }
            """);

        var result = RestTransactionResultProjector.Project(transaction);

        var exercised = result.ExercisedEvents.Should().ContainSingle().Subject;
        exercised.ContractId.Should().Be("00holding");
        exercised.ChoiceName.Should().Be("Archive");
        exercised.Consuming.Should().BeTrue();
        exercised.ActingParties.Should().ContainSingle().Which.Should().Be((Party)"alice::ns1");
    }

    [Fact]
    public void ProjectToContractId_returns_One_when_exactly_one_created_contract_matches_the_marker()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.One(new TransactionResult(
            "upd-1",
            LedgerOffset.At(1),
            [
                new CreatedContract(
                    "0",
                    "00holding",
                    new RuntimeIdentifier("tmpl-pkg", "Sample.Token", "Holding"),
                    DamlRecord.Create(),
                    [],
                    [],
                    [],
                    ContractKey: null),
            ],
            [],
            new CommandId("cmd-1")));

        var projected = RestTransactionResultProjector.ProjectToContractId<TemplateMarker>(outcome);

        var one = projected.Should().BeOfType<ExerciseOutcome<ContractId<TemplateMarker>>.One>().Subject;
        one.Result.Value.Should().Be("00holding");
    }

    [Fact]
    public void ProjectToContractId_returns_None_when_no_created_contract_matches_the_marker()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.One(new TransactionResult(
            "upd-1", LedgerOffset.At(1), [], [], new CommandId("cmd-1")));

        var projected = RestTransactionResultProjector.ProjectToContractId<TemplateMarker>(outcome);

        projected.Should().BeOfType<ExerciseOutcome<ContractId<TemplateMarker>>.None>();
    }

    [Fact]
    public void ProjectToContractId_passes_through_a_DamlError_outcome()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateOther, "SOME_ERROR", "boom", new Dictionary<string, string>());

        var projected = RestTransactionResultProjector.ProjectToContractId<TemplateMarker>(outcome);

        var error = projected.Should().BeOfType<ExerciseOutcome<ContractId<TemplateMarker>>.DamlError>().Subject;
        error.ErrorId.Should().Be("SOME_ERROR");
    }

    [Fact]
    public void ProjectToContractId_passes_through_an_InfraError_outcome()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.InfraError(503, "unavailable");

        var projected = RestTransactionResultProjector.ProjectToContractId<TemplateMarker>(outcome);

        var error = projected.Should().BeOfType<ExerciseOutcome<ContractId<TemplateMarker>>.InfraError>().Subject;
        error.StatusCode.Should().Be(503);
    }

    [Fact]
    public void ProjectChoiceResult_decodes_the_single_matching_exercised_events_result()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.One(new TransactionResult(
            "upd-1", LedgerOffset.At(1), [], [], new CommandId("cmd-1"))
        {
            ExercisedEvents =
            [
                new ExercisedEvent(
                    "00holding",
                    new RuntimeIdentifier("tmpl-pkg", "Sample.Token", "Holding"),
                    null,
                    "GetOwner",
                    DamlUnit.Instance,
                    new DamlParty("alice::ns1"),
                    false,
                    [(Party)"alice::ns1"],
                    [(Party)"alice::ns1"]),
            ],
        });

        var projected = RestTransactionResultProjector.ProjectChoiceResult<Party>(outcome, new ChoiceName("GetOwner"));

        var one = projected.Should().BeOfType<ExerciseOutcome<Party>.One>().Subject;
        one.Result.Should().Be((Party)"alice::ns1");
    }

    [Fact]
    public void Project_still_accepts_a_transaction_the_wire_carried_no_command_id_on()
    {
        var transaction = TransactionFrom(
            """
            {
              "transaction": {
                "updateId": "upd-1",
                "offset": "42",
                "events": []
              }
            }
            """);

        var act = () => RestTransactionResultProjector.Project(transaction);

        act.Should().NotThrow(
                "Transaction.commandId is optional on the wire and is absent for everyone except the "
                + "submitting party, so the transaction path must keep tolerating its absence")
            .Which.CommandId.Should().BeNull();
    }

    private const string IdiomaticExerciseResultTransaction =
        """
        {
          "transaction": {
            "updateId": "upd-1",
            "offset": "1",
            "events": [
              {
                "ExercisedEvent": {
                  "offset": "1",
                  "contractId": "00holding",
                  "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                  "choice": "Split",
                  "choiceArgument": {},
                  "actingParties": ["alice::ns1"],
                  "consuming": false,
                  "witnessParties": ["alice::ns1"],
                  "exerciseResult": {"owner": "alice::ns1", "amount": "10.5"}
                }
              }
            ]
          }
        }
        """;

    [Fact]
    public void Project_still_fails_the_untyped_path_on_an_idiomatic_record_shaped_exerciseResult()
    {
        var transaction = TransactionFrom(IdiomaticExerciseResultTransaction);

        var act = () => RestTransactionResultProjector.Project(transaction);

        act.Should().Throw<MalformedResponseException>()
            .Which.Message.Should().Be(
                "Malformed response from ledger: Received a wire Value with no recognisable sum case set.");
    }

    [Fact]
    public void ProjectForChoiceResult_decodes_the_matching_choices_idiomatic_result_where_the_untyped_path_would_fail()
    {
        var transaction = TransactionFrom(IdiomaticExerciseResultTransaction);

        var result = RestTransactionResultProjector.ProjectForChoiceResult<CirceMarker>(
            transaction, new ChoiceName("Split"));

        var exercised = result.ExercisedEvents.Should().ContainSingle().Subject;
        var record = exercised.ExerciseResult.Should().BeOfType<DamlRecord>().Subject;
        record.GetRequiredField("owner").As<DamlParty>().Value.Should().Be("alice::ns1");
        record.GetRequiredField("amount").As<DamlNumeric>().Value.Should().Be(10.5m);
    }

    private const string BareEmptyObjectExerciseResultTransaction =
        """
        {
          "transaction": {
            "updateId": "upd-1",
            "offset": "1",
            "events": [
              {
                "ExercisedEvent": {
                  "offset": "1",
                  "contractId": "00holding",
                  "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                  "choice": "Settle",
                  "choiceArgument": {},
                  "actingParties": ["alice::ns1"],
                  "consuming": false,
                  "witnessParties": ["alice::ns1"],
                  "exerciseResult": {}
                }
              }
            ]
          }
        }
        """;

    [Fact]
    public void Project_applies_WireUnitEncoding_to_a_bare_empty_object_exerciseResult_on_the_untyped_path()
    {
        var transaction = TransactionFrom(BareEmptyObjectExerciseResultTransaction);

        var result = RestTransactionResultProjector.Project(transaction);

        result.ExercisedEvents.Should().ContainSingle()
            .Which.ExerciseResult.Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void ProjectForChoiceResult_decodes_a_bare_empty_object_exerciseResult_as_the_empty_record_the_choice_returns()
    {
        var transaction = TransactionFrom(BareEmptyObjectExerciseResultTransaction);

        var result = RestTransactionResultProjector.ProjectForChoiceResult<CirceReceipt>(
            transaction, new ChoiceName("Settle"));

        result.ExercisedEvents.Should().ContainSingle()
            .Which.ExerciseResult.Should().BeOfType<DamlRecord>()
            .Which.Fields.Should().BeEmpty();
    }

    [Fact]
    public void ExerciseResult_hands_back_the_empty_record_rather_than_null_for_a_bare_empty_object_result()
    {
        var transaction = TransactionFrom(BareEmptyObjectExerciseResultTransaction);

        var result = RestTransactionResultProjector.ProjectForChoiceResult<CirceReceipt>(
            transaction, new ChoiceName("Settle"));

        result.ExerciseResult<DamlRecord>("Settle").Should().NotBeNull()
            .And.Subject.As<DamlRecord>().Fields.Should().BeEmpty();
    }

    [Fact]
    public void ProjectForCreatedTemplate_decodes_the_matching_created_events_argument_against_the_template_type()
    {
        var transaction = TransactionFrom(
            """
            {
              "transaction": {
                "updateId": "upd-1",
                "offset": "1",
                "events": [
                  {
                    "CreatedEvent": {
                      "offset": "1",
                      "contractId": "00holding",
                      "nodeId": 0,
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                      "createArgument": {"owner": "alice::ns1", "amount": "10.5"}
                    }
                  }
                ]
              }
            }
            """);

        var result = RestTransactionResultProjector.ProjectForCreatedTemplate<CirceMarker>(transaction);

        var created = result.CreatedContracts.Should().ContainSingle().Subject;
        created.Payload.GetRequiredField("owner").As<DamlParty>().Value.Should().Be("alice::ns1");
        created.Payload.GetRequiredField("amount").As<DamlNumeric>().Value.Should().Be(10.5m);
    }

    [Fact]
    public void ProjectForCreatedTemplate_decodes_a_bare_scalar_wire_contract_key_against_the_templates_key_type()
    {
        var transaction = TransactionFrom(
            """
            {
              "transaction": {
                "updateId": "upd-1",
                "offset": "1",
                "events": [
                  {
                    "CreatedEvent": {
                      "offset": "1",
                      "contractId": "00keyed",
                      "nodeId": 0,
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                      "createArgument": {"fields": [{"label": "owner", "value": {"party": "alice::ns1"}}]},
                      "contractKey": "alice::ns1"
                    }
                  }
                ]
              }
            }
            """);

        var result = RestTransactionResultProjector.ProjectForCreatedTemplate<ScalarKeyedMarker>(transaction);

        var created = result.CreatedContracts.Should().ContainSingle().Subject;
        created.ContractKey.Should().NotBeNull();
        created.ContractKey!.Value.Should().Be(new DamlParty("alice::ns1"));
    }
}
