// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

/// <summary>
/// The exercise results the shared <c>RichTypes:GenericResults</c> template answers with, read back
/// through the client against the generated choice descriptors and nothing else: every result below
/// used to be refused by a catch that guessed at generic shapes, and now decodes because the
/// descriptor's own reader knows the Daml type.
/// </summary>
public sealed class RestGenericChoiceResultDecodeTests : IDisposable
{
    private const string GenericResultsPackageId = "d3b1c254073af761246f22060c60d08a92c4cf4d59327c7a554d3ec6d3e794ec";
    private static readonly Party Alice = new("party::alice");

    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    private static string ExercisedTransaction(string choice, string choiceArgument, string exerciseResult) =>
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
                  "contractId": "00generic",
                  "templateId": {"packageId": "{{GenericResultsPackageId}}", "moduleName": "RichTypes", "entityName": "GenericResults"},
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

    private async Task<ExerciseOutcome<DamlValue>> ExerciseAsync(string choice, string choiceArgument, string exerciseResult)
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, ExercisedTransaction(choice, choiceArgument, exerciseResult));
        var factory = new StubHttpClientFactory(transport);
        _factories.Add(factory);
        var client = new RestLedgerClient(factory);
        var command = ExerciseCommand.For(
            new ContractId<GenericResults>("00generic"), new ChoiceName(choice), DamlUnit.Instance);

        return await client.TryExerciseAsync<DamlValue>(
            command, Alice, cancellationToken: TestContext.Current.CancellationToken);
    }

    private async Task<DamlValue> ExercisedResultAsync(string choice, string choiceArgument, string exerciseResult)
    {
        var outcome = await ExerciseAsync(choice, choiceArgument, exerciseResult);

        return outcome.Should().BeOfType<ExerciseOutcome<DamlValue>.One>("the participant answered a shape the descriptor reads").Subject.Result;
    }

    [Fact]
    public async Task A_TextMap_choice_answering_an_empty_object_yields_an_empty_DamlTextMap_rather_than_unit()
    {
        var result = await ExercisedResultAsync("ReturnTextMap", "{}", "{}");

        result.Should().BeOfType<DamlTextMap>().Which.Values.Should().BeEmpty();
    }

    [Fact]
    public async Task A_TextMap_choice_answering_entries_yields_them_with_their_Int64_values()
    {
        var result = await ExercisedResultAsync("ReturnTextMap", "{}", """{"a": "1", "b": "22"}""");

        var map = result.Should().BeOfType<DamlTextMap>().Subject;
        map.Values.Should().HaveCount(2);
        map.Values["a"].Should().BeOfType<DamlInt64>().Which.Value.Should().Be(1L);
        map.Values["b"].Should().BeOfType<DamlInt64>().Which.Value.Should().Be(22L);
    }

    [Fact]
    public async Task A_list_choice_answering_an_empty_array_yields_an_empty_DamlList()
    {
        var result = await ExercisedResultAsync("ReturnContractIds", "{}", "[]");

        result.Should().BeOfType<DamlList>().Which.Values.Should().BeEmpty();
    }

    [Fact]
    public async Task A_list_choice_answering_contract_ids_yields_them_in_order()
    {
        var result = await ExercisedResultAsync("ReturnContractIds", "{}", """["00a", "00b"]""");

        var list = result.Should().BeOfType<DamlList>().Subject;
        list.Values.Should().HaveCount(2);
        list.Values[0].Should().BeOfType<DamlContractId>().Which.Value.Should().Be("00a");
        list.Values[1].Should().BeOfType<DamlContractId>().Which.Value.Should().Be("00b");
    }

    [Fact]
    public async Task An_optional_choice_answering_null_yields_an_empty_DamlOptional()
    {
        var result = await ExercisedResultAsync("ReturnOptionalText", """{"wantSome": false}""", "null");

        result.Should().BeOfType<DamlOptional>().Which.Value.Should().BeNull();
    }

    [Fact]
    public async Task An_optional_choice_answering_a_value_yields_it_inside_the_DamlOptional()
    {
        var result = await ExercisedResultAsync("ReturnOptionalText", """{"wantSome": true}""", "\"hello\"");

        result.Should().BeOfType<DamlOptional>().Which.Value.Should().BeOfType<DamlText>().Which.Value.Should().Be("hello");
    }

    [Fact]
    public async Task A_tuple_choice_answering_its_two_fields_yields_a_record_holding_both()
    {
        var result = await ExercisedResultAsync("ReturnTuple", "{}", """{"_1": "left", "_2": "7"}""");

        var tuple = result.Should().BeOfType<DamlRecord>().Subject;
        tuple.GetRequiredField("_1").Should().BeOfType<DamlText>().Which.Value.Should().Be("left");
        tuple.GetRequiredField("_2").Should().BeOfType<DamlInt64>().Which.Value.Should().Be(7L);
    }

    [Fact]
    public async Task A_nested_optional_choice_keeps_the_inner_absence_distinct_from_the_outer_absence()
    {
        var outerNone = await ExercisedResultAsync("ReturnNestedOptional", """{"outer": false, "inner": false}""", "[]");
        var innerNone = await ExercisedResultAsync("ReturnNestedOptional", """{"outer": true, "inner": false}""", "[[]]");
        var innerSome = await ExercisedResultAsync("ReturnNestedOptional", """{"outer": true, "inner": true}""", """[["deep"]]""");

        outerNone.Should().BeOfType<DamlOptionalChain>().Which.Value.Should().BeNull();
        innerNone.Should().BeOfType<DamlOptionalChain>().Which.Value.Should().BeOfType<DamlOptionalChain>()
            .Which.Value.Should().BeNull();
        innerSome.Should().BeOfType<DamlOptionalChain>().Which.Value.Should().BeOfType<DamlOptionalChain>()
            .Which.Value.Should().BeOfType<DamlText>().Which.Value.Should().Be("deep");
    }

    [Fact]
    public async Task An_Either_choice_answering_a_tagged_variant_yields_the_chosen_arm()
    {
        var result = await ExercisedResultAsync("ReturnEither", """{"wantRight": true}""", """{"tag": "Right", "value": "9"}""");

        var variant = result.Should().BeOfType<DamlVariant>().Subject;
        variant.Constructor.Should().Be("Right");
        variant.Value.Should().BeOfType<DamlInt64>().Which.Value.Should().Be(9L);
    }

    [Fact]
    public async Task The_unit_returning_Archive_choice_yields_DamlUnit_from_its_empty_object_result()
    {
        var result = await ExercisedResultAsync("Archive", "{}", "{}");

        result.Should().BeOfType<DamlUnit>();
    }

    [Fact]
    public async Task A_result_the_descriptor_cannot_read_is_reported_as_committed_but_undecodable()
    {
        var outcome = await ExerciseAsync("ReturnTextMap", "{}", """{"a": "not-a-number"}""");

        var error = outcome.Should().BeOfType<ExerciseOutcome<DamlValue>.CommittedUndecodable>().Subject;
        error.SourceException.Should().BeOfType<MalformedResponseException>();
        error.Message.Should().StartWith("Server returned a malformed transaction: Malformed response from ledger: ");
    }
}
