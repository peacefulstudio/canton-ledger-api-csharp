// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;
using WireTransaction = Canton.Ledger.Rest.Client.Raw.SubmitAndWaitForTransactionResponse;

namespace Canton.Ledger.Rest.Client.Tests;

/// <summary>
/// Choices the generated <c>RichTypes:Holding</c> interface declares, exercised on an <c>Asset</c>
/// through that interface and read back over REST against the interface's own choice descriptors.
/// </summary>
public sealed class RestInterfaceChoiceDecodeTests : IDisposable
{
    private const string RichTypesPackageId = "d3b1c254073af761246f22060c60d08a92c4cf4d59327c7a554d3ec6d3e794ec";
    private static readonly Party Alice = new("party::alice");

    // DamlTypeResolver indexes only already-loaded assemblies, so the generated
    // conformance assembly must be loaded before the first resolve in this process.
    static RestInterfaceChoiceDecodeTests() =>
        RuntimeHelpers.RunClassConstructor(typeof(IHolding).TypeHandle);

    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    private static string InterfaceExercisedTransaction(
        string interfacePackageId, string choice, string choiceArgument, string exerciseResult) =>
        $$"""
        {
          "transaction": {
            "updateId": "upd-1",
            "offset": "1",
            "events": [
              {
                "ExercisedEvent": {
                  "offset": "1",
                  "nodeId": 0,
                  "contractId": "00asset",
                  "templateId": {"packageId": "{{RichTypesPackageId}}", "moduleName": "RichTypes", "entityName": "Asset"},
                  "interfaceId": {"packageId": "{{interfacePackageId}}", "moduleName": "RichTypes", "entityName": "Holding"},
                  "choice": "{{choice}}",
                  "choiceArgument": {{choiceArgument}},
                  "actingParties": ["party::alice"],
                  "consuming": false,
                  "witnessParties": ["party::alice"],
                  "exerciseResult": {{exerciseResult}}
                }
              }
            ]
          }
        }
        """;

    private static ExercisedEvent ProjectedExercise(
        string interfacePackageId, string choice, string choiceArgument, string exerciseResult)
    {
        var response = JsonSerializer.Deserialize<WireTransaction>(
            InterfaceExercisedTransaction(interfacePackageId, choice, choiceArgument, exerciseResult),
            RestRefitSettings.SerializerOptions);

        return RestTransactionResultProjector.Project(response!.Transaction).ExercisedEvents.Should().ContainSingle().Subject;
    }

    private async Task<ExerciseOutcome<DamlValue>> ExerciseAsync(string choice, string choiceArgument, string exerciseResult)
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, InterfaceExercisedTransaction(RichTypesPackageId, choice, choiceArgument, exerciseResult));
        var factory = new StubHttpClientFactory(transport);
        _factories.Add(factory);
        var client = new RestLedgerClient(factory);
        var command = ExerciseCommand.For(
            new ContractId<IHolding>("00asset"), new ChoiceName(choice), DamlUnit.Instance);

        return await client.TryExerciseAsync<DamlValue>(
            command, Alice, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Project_decodes_the_argument_and_result_of_an_interface_choice_with_a_record_argument_and_text_result()
    {
        var exercised = ProjectedExercise(RichTypesPackageId, "Describe", """{"prefix": "audit"}""", "\"audit: 12.5\"");

        exercised.ChoiceName.Should().Be("Describe");
        exercised.ChoiceArgument.Should().BeOfType<DamlRecord>()
            .Which.Fields.Should().ContainSingle()
            .Which.Should().Be(new DamlField("prefix", new DamlText("audit")));
        exercised.ExerciseResult.Should().Be(new DamlText("audit: 12.5"));
    }

    [Fact]
    public void Project_decodes_the_argument_and_result_of_an_interface_choice_with_an_Int64_argument_and_a_list_result()
    {
        var exercised = ProjectedExercise(RichTypesPackageId, "Split", """{"pieces": "3"}""", """["00a", "00b"]""");

        exercised.ChoiceArgument.Should().BeOfType<DamlRecord>()
            .Which.Fields.Should().ContainSingle()
            .Which.Should().Be(new DamlField("pieces", new DamlInt64(3)));
        var result = exercised.ExerciseResult.Should().BeOfType<DamlList>().Subject;
        result.Values.Should().Equal(new DamlContractId("00a"), new DamlContractId("00b"));
    }

    [Fact]
    public void Project_decodes_the_interface_Archive_choice_to_an_empty_record_argument_and_a_unit_result()
    {
        var exercised = ProjectedExercise(RichTypesPackageId, "Archive", "{}", "{}");

        exercised.ChoiceArgument.Should().BeOfType<DamlRecord>().Which.Fields.Should().BeEmpty();
        exercised.ExerciseResult.Should().BeOfType<DamlUnit>();
    }

    [Fact]
    public void Project_resolves_the_interface_under_another_package_id_by_its_module_and_entity()
    {
        var exercised = ProjectedExercise("upgraded-pkg", "Reissue", """{"newAmount": "7.5"}""", "\"00reissued\"");

        exercised.ChoiceArgument.Should().BeOfType<DamlRecord>()
            .Which.Fields.Should().ContainSingle()
            .Which.Should().Be(new DamlField("newAmount", new DamlNumeric(7.5m)));
        exercised.ExerciseResult.Should().Be(new DamlContractId("00reissued"));
    }

    [Fact]
    public void Project_refuses_a_choice_the_loaded_interface_does_not_declare()
    {
        var act = () => ProjectedExercise(RichTypesPackageId, "Burn", "{}", "{}");

        var refusal = act.Should().Throw<TemplateTypeRequiredException>().Which;
        refusal.TypeId.Should().Be($"{RichTypesPackageId}:RichTypes:Holding");
        refusal.ChoiceName.Should().Be("Burn");
    }

    [Fact]
    public async Task TryExerciseAsync_yields_the_interface_choice_result_as_a_DamlValue()
    {
        var outcome = await ExerciseAsync("Describe", """{"prefix": "audit"}""", "\"audit: 12.5\"");

        outcome.Should().BeOfType<ExerciseOutcome<DamlValue>.One>().Which.Result.Should().Be(new DamlText("audit: 12.5"));
    }

    [Fact]
    public async Task TryExerciseAsync_reports_CommittedUndecodable_when_interface_choice_argument_is_unreadable()
    {
        var outcome = await ExerciseAsync("Split", """{"pieces": "not-a-number"}""", "[]");

        var error = outcome.Should().BeOfType<ExerciseOutcome<DamlValue>.CommittedUndecodable>().Subject;
        error.SourceException.Should().BeOfType<MalformedResponseException>();
        error.Message.Should().StartWith("Server returned a malformed transaction: Malformed response from ledger: ");
    }
}
