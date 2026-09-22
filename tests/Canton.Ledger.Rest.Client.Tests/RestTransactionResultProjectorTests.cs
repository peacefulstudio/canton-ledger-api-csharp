// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Serialization;
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
    private sealed record TransferArgument(
        [property: DamlFieldAttribute("newOwner")] Party NewOwner) : IDamlRecord<TransferArgument>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("newOwner", NewOwner.ToDamlValue()));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(
                json,
                context,
                ("newOwner", DamlLfJsonDecoders.ReadParty));
        public static TransferArgument FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("newOwner").As<DamlParty>()));
    }

    private sealed record TemplateMarker(
        [property: DamlFieldAttribute("owner")] Party Owner) : ITemplate, IDamlRecord<TemplateMarker>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "ProjectedHolding");
        public static string PackageId => "tmpl-pkg";
        public static string PackageName => "token-impl";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public static Choice<TemplateMarker, DamlUnit, DamlUnit> ChoiceArchive { get; } = new()
        {
            Name = new ChoiceName("Archive"),
            Consuming = true,
            ArgumentEncoder = unit => unit,
            ResultDecoder = result => result.As<DamlUnit>(),
            ArgumentDecoder = value => value.As<DamlUnit>(),
            ArgumentJsonReader = DamlLfJsonDecoders.ReadUnit,
            ResultJsonReader = DamlLfJsonDecoders.ReadUnit,
        };

        public static Choice<TemplateMarker, TransferArgument, Party> ChoiceTransfer { get; } = new()
        {
            Name = new ChoiceName("Transfer"),
            Consuming = true,
            ArgumentEncoder = argument => argument.ToRecord(),
            ResultDecoder = result => Party.FromDamlValue(result.As<DamlParty>()),
            ArgumentDecoder = value => TransferArgument.FromRecord(value.As<DamlRecord>()),
            ArgumentJsonReader = TransferArgument.__ReadDamlLfJson,
            ResultJsonReader = DamlLfJsonDecoders.ReadParty,
        };

        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("owner", Owner.ToDamlValue()));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(
                json,
                context,
                ("owner", DamlLfJsonDecoders.ReadParty));
        public static TemplateMarker FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()));
    }

    private sealed record ScalarKeyedMarker(
        [property: DamlFieldAttribute("owner")] Party Owner)
        : ITemplate, IDamlRecord<ScalarKeyedMarker>, IHasKey<ScalarKeyedMarker, Party>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "KeyedHolding");
        public static string PackageId => "tmpl-pkg";
        public static string PackageName => "token-impl";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("owner", Owner.ToDamlValue()));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(
                json,
                context,
                ("owner", DamlLfJsonDecoders.ReadParty));
        public static ScalarKeyedMarker FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()));

        public static KeyDescriptor<ScalarKeyedMarker, Party> Key { get; } = new()
        {
            KeyEncoder = owner => owner.ToDamlValue(),
            KeyDecoder = value => Party.FromDamlValue(value.As<DamlParty>()),
            KeyJsonReader = DamlLfJsonDecoders.ReadParty,
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
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "ProjectedHolding"},
                      "createArgument": {"owner": "alice::ns1"},
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
        created.TemplateId.Should().Be(new RuntimeIdentifier("tmpl-pkg", "Sample.Token", "ProjectedHolding"));
        created.Payload.GetRequiredField("owner").Should().Be(new DamlParty("alice::ns1"));
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
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "KeyedHolding"},
                      "createArgument": {"owner": "alice::ns1"},
                      "contractKey": "alice::ns1",
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
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "KeyedHolding"},
                      "createArgument": {"owner": "alice::ns1"},
                      "contractKey": "alice::ns1"
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
    public void Project_reads_an_explicit_null_contract_key_as_no_key()
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
                      "contractId": "00keyedWithANullKey",
                      "nodeId": 0,
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "KeyedHolding"},
                      "createArgument": {"owner": "alice::ns1"},
                      "contractKey": null
                    }
                  }
                ]
              }
            }
            """);

        var result = RestTransactionResultProjector.Project(transaction);

        var created = result.CreatedContracts.Should().ContainSingle().Subject;
        created.ContractId.Should().Be("00keyedWithANullKey");
        created.ContractKey.Should().BeNull();
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
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "ProjectedHolding"},
                      "createArgument": {"owner": "alice::ns1"}
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
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "ProjectedHolding"},
                      "choice": "Archive",
                      "choiceArgument": {},
                      "actingParties": ["alice::ns1"],
                      "consuming": true,
                      "witnessParties": ["alice::ns1"],
                      "exerciseResult": {}
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
        exercised.ChoiceArgument.Should().Be(DamlUnit.Instance);
        exercised.ExerciseResult.Should().Be(DamlUnit.Instance);
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
                    new RuntimeIdentifier("tmpl-pkg", "Sample.Token", "ProjectedHolding"),
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
                    new RuntimeIdentifier("tmpl-pkg", "Sample.Token", "ProjectedHolding"),
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
                  "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "CirceHolding"},
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
    public void Project_decodes_a_record_shaped_exerciseResult_against_the_loaded_choice_result_type()
    {
        var transaction = TransactionFrom(IdiomaticExerciseResultTransaction);

        var result = RestTransactionResultProjector.Project(transaction);

        var record = result.ExercisedEvents.Should().ContainSingle()
            .Which.ExerciseResult.Should().BeOfType<DamlRecord>().Subject;
        record.GetRequiredField("owner").Should().Be(new DamlParty("alice::ns1"));
        record.GetRequiredField("amount").Should().Be(new DamlNumeric(10.5m));
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
                  "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "CirceHolding"},
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
    public void Project_decodes_a_bare_empty_object_exerciseResult_as_the_empty_record_the_choice_returns()
    {
        var transaction = TransactionFrom(BareEmptyObjectExerciseResultTransaction);

        var result = RestTransactionResultProjector.Project(transaction);

        result.ExercisedEvents.Should().ContainSingle()
            .Which.ExerciseResult.Should().BeOfType<DamlRecord>()
            .Which.Fields.Should().BeEmpty();
    }

    [Fact]
    public void ExerciseResult_hands_back_the_empty_record_rather_than_null_for_a_bare_empty_object_result()
    {
        var transaction = TransactionFrom(BareEmptyObjectExerciseResultTransaction);

        var result = RestTransactionResultProjector.Project(transaction);

        result.ExerciseResult<DamlRecord>("Settle").Should().NotBeNull()
            .And.Subject.As<DamlRecord>().Fields.Should().BeEmpty();
    }

    [Fact]
    public void Project_decodes_a_created_events_argument_against_the_loaded_template_type()
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
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "CirceHolding"},
                      "createArgument": {"owner": "alice::ns1", "amount": "10.5"}
                    }
                  }
                ]
              }
            }
            """);

        var result = RestTransactionResultProjector.Project(transaction);

        var created = result.CreatedContracts.Should().ContainSingle().Subject;
        created.Payload.GetRequiredField("owner").Should().Be(new DamlParty("alice::ns1"));
        created.Payload.GetRequiredField("amount").Should().Be(new DamlNumeric(10.5m));
    }

    [Fact]
    public void Project_decodes_a_bare_scalar_wire_contract_key_against_the_loaded_templates_key_type()
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
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "KeyedHolding"},
                      "createArgument": {"owner": "alice::ns1"},
                      "contractKey": "alice::ns1"
                    }
                  }
                ]
              }
            }
            """);

        var result = RestTransactionResultProjector.Project(transaction);

        var created = result.CreatedContracts.Should().ContainSingle().Subject;
        created.ContractKey.Should().Be(new ContractKey(
            new DamlParty("alice::ns1"), new RuntimeIdentifier("tmpl-pkg", "Sample.Token", "KeyedHolding")));
    }

    [Fact]
    public void Project_decodes_every_payload_of_a_mixed_transaction_against_its_loaded_generated_type()
    {
        var transaction = TransactionFrom(
            """
            {
              "transaction": {
                "updateId": "upd-mixed",
                "commandId": "cmd-mixed",
                "offset": "77",
                "events": [
                  {"ArchivedEvent": {"offset": "77", "nodeId": 0, "contractId": "00a"}},
                  {
                    "CreatedEvent": {
                      "offset": "77",
                      "contractId": "00b",
                      "nodeId": 1,
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "ProjectedHolding"},
                      "createArgument": {"owner": "bob::ns1"}
                    }
                  },
                  {
                    "CreatedEvent": {
                      "offset": "77",
                      "contractId": "00c",
                      "nodeId": 2,
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "KeyedHolding"},
                      "createArgument": {"owner": "carol::ns1"},
                      "contractKey": "carol::ns1",
                      "contractKeyHash": "AQID"
                    }
                  },
                  {
                    "ExercisedEvent": {
                      "offset": "77",
                      "nodeId": 3,
                      "contractId": "00a",
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "ProjectedHolding"},
                      "choice": "Transfer",
                      "choiceArgument": {"newOwner": "bob::ns1"},
                      "actingParties": ["alice::ns1"],
                      "consuming": true,
                      "witnessParties": ["alice::ns1"],
                      "exerciseResult": "bob::ns1"
                    }
                  }
                ]
              }
            }
            """);

        var result = RestTransactionResultProjector.Project(transaction);

        result.UpdateId.Should().Be("upd-mixed");
        result.ArchivedContractIds.Should().Equal("00a");
        result.CreatedContracts.Should().SatisfyRespectively(
            b =>
            {
                b.ContractId.Should().Be("00b");
                b.TemplateId.Should().Be(new RuntimeIdentifier("tmpl-pkg", "Sample.Token", "ProjectedHolding"));
                b.Payload.Fields.Should().ContainSingle()
                    .Which.Should().Be(new DamlField("owner", new DamlParty("bob::ns1")));
                b.ContractKey.Should().BeNull();
            },
            c =>
            {
                c.ContractId.Should().Be("00c");
                c.TemplateId.Should().Be(new RuntimeIdentifier("tmpl-pkg", "Sample.Token", "KeyedHolding"));
                c.Payload.Fields.Should().ContainSingle()
                    .Which.Should().Be(new DamlField("owner", new DamlParty("carol::ns1")));
                c.ContractKey.Should().Be(new ContractKey(
                    new DamlParty("carol::ns1"), new RuntimeIdentifier("tmpl-pkg", "Sample.Token", "KeyedHolding")));
                c.ContractKey!.KeyHash.Should().Be("AQID");
            });
        var exercised = result.ExercisedEvents.Should().ContainSingle().Subject;
        exercised.ContractId.Should().Be("00a");
        exercised.ChoiceName.Should().Be("Transfer");
        exercised.ChoiceArgument.Should().BeOfType<DamlRecord>()
            .Which.Fields.Should().ContainSingle()
            .Which.Should().Be(new DamlField("newOwner", new DamlParty("bob::ns1")));
        exercised.ExerciseResult.Should().Be(new DamlParty("bob::ns1"));
    }

    [Fact]
    public void Project_refuses_a_created_event_whose_template_id_no_loaded_generated_type_declares()
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
                      "contractId": "00unknown",
                      "nodeId": 0,
                      "templateId": {"packageId": "missing-pkg", "moduleName": "Missing.Module", "entityName": "Missing"},
                      "createArgument": {"owner": "alice::ns1"}
                    }
                  }
                ]
              }
            }
            """);

        var act = () => RestTransactionResultProjector.Project(transaction);

        var refusal = act.Should().Throw<TemplateTypeRequiredException>().Which;
        refusal.TypeId.Should().Be("missing-pkg:Missing.Module:Missing");
        refusal.ChoiceName.Should().BeNull();
        refusal.Message.Should().Be(
            "No generated type is loaded for 'missing-pkg:Missing.Module:Missing'; load exactly one assembly generated for its Daml package before reading this payload over the JSON Ledger API.");
    }

    [Fact]
    public void Project_refuses_an_exercised_event_whose_choice_the_loaded_template_declares_no_descriptor_for()
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
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "ProjectedHolding"},
                      "choice": "Burn",
                      "choiceArgument": {},
                      "actingParties": ["alice::ns1"],
                      "consuming": true,
                      "witnessParties": ["alice::ns1"],
                      "exerciseResult": {}
                    }
                  }
                ]
              }
            }
            """);

        var act = () => RestTransactionResultProjector.Project(transaction);

        var refusal = act.Should().Throw<TemplateTypeRequiredException>().Which;
        refusal.TypeId.Should().Be("tmpl-pkg:Sample.Token:ProjectedHolding");
        refusal.ChoiceName.Should().Be("Burn");
    }

    [Fact]
    public void Project_refuses_an_interface_choice_exercise_naming_the_interface_and_the_choice()
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
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "ProjectedHolding"},
                      "interfaceId": {"packageId": "iface-pkg", "moduleName": "Token.Api", "entityName": "IHolding"},
                      "choice": "Lock",
                      "choiceArgument": {"reason": "audit"},
                      "actingParties": ["alice::ns1"],
                      "consuming": false,
                      "witnessParties": ["alice::ns1"],
                      "exerciseResult": {}
                    }
                  }
                ]
              }
            }
            """);

        var act = () => RestTransactionResultProjector.Project(transaction);

        var refusal = act.Should().Throw<TemplateTypeRequiredException>().Which;
        refusal.TypeId.Should().Be("iface-pkg:Token.Api:IHolding");
        refusal.ChoiceName.Should().Be("Lock");
        refusal.Message.Should().Be(
            "No generated type is loaded for choice 'Lock' of 'iface-pkg:Token.Api:IHolding'; load exactly one assembly generated for its Daml package before reading this payload over the JSON Ledger API.");
    }

    [Theory]
    [InlineData("choiceArgument", "\"exerciseResult\": {}")]
    [InlineData("exerciseResult", "\"choiceArgument\": {}")]
    public void Project_throws_a_malformed_response_for_an_exercised_event_missing_a_payload(
        string missingField, string presentPayloadJson)
    {
        var transaction = TransactionFrom(
            $$"""
            {
              "transaction": {
                "updateId": "upd-1",
                "offset": "1",
                "events": [
                  {
                    "ExercisedEvent": {
                      "offset": "1",
                      "contractId": "00holding",
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "ProjectedHolding"},
                      "choice": "Archive",
                      "actingParties": ["alice::ns1"],
                      "consuming": true,
                      "witnessParties": ["alice::ns1"],
                      {{presentPayloadJson}}
                    }
                  }
                ]
              }
            }
            """);

        var act = () => RestTransactionResultProjector.Project(transaction);

        act.Should().Throw<MalformedResponseException>().Which.Message.Should().Be(
            $"Malformed response from ledger: ExercisedEvent for contract '00holding' has no {missingField}, though the Ledger API marks the field as required.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"createArgument\": null,")]
    public void Project_throws_a_malformed_response_for_a_created_event_missing_its_create_argument(string createArgumentJson)
    {
        var transaction = TransactionFrom(
            $$"""
            {
              "transaction": {
                "updateId": "upd-1",
                "offset": "1",
                "events": [
                  {
                    "CreatedEvent": {
                      {{createArgumentJson}}
                      "offset": "1",
                      "contractId": "00holding",
                      "nodeId": 0,
                      "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "ProjectedHolding"}
                    }
                  }
                ]
              }
            }
            """);

        var act = () => RestTransactionResultProjector.Project(transaction);

        act.Should().Throw<MalformedResponseException>().Which.Message.Should().Be(
            "Malformed response from ledger: CreatedEvent for contract '00holding' has no createArgument, though the Ledger API marks the field as required.");
    }
}
