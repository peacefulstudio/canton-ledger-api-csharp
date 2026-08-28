// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
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
    private sealed record TemplateMarker : ITemplate
    {
        public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "Holding");
        public static string PackageId => "tmpl-pkg";
        public static string PackageName => "token-impl";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, []);
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
        result.CommandId.Value.Should().Be("cmd-1");
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
        created.Payload.Should().Contain("alice::ns1");
        created.InterfaceIds.Should().ContainSingle()
            .Which.Should().Be(new RuntimeIdentifier("iface-pkg", "Sample.Token", "IHolding"));
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
                "events": [{"CreatedEvent": {"offset": "1", "contractId": "00noTemplateId"}}]
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
            [new CreatedContract("00holding", new RuntimeIdentifier("tmpl-pkg", "Sample.Token", "Holding"), "{}")],
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
            .Which.CommandId.Should().Be(default(CommandId));
    }
}
