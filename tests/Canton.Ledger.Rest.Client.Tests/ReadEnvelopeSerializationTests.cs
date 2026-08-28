// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class ReadEnvelopeSerializationTests
{
    private const string ActiveContractEntry = """
        {"workflowId":"","contractEntry":{"JsActiveContract":{
          "createdEvent":{"offset":31,"nodeId":0,"contractId":"0005","templateId":"pkg:RichTypes:Marker",
                          "createArgument":{"owner":"alice::1220ab"},"observers":[],"signatories":[]},
          "synchronizerId":"global-domain::1220cd","reassignmentCounter":0}}}
        """;

    private const string TransactionUpdate = """
        {"update":{"Transaction":{"value":{
          "updateId":"1220ef","commandId":"c-1","workflowId":"","offset":9728,
          "synchronizerId":"global-domain::1220cd","effectiveAt":"2026-07-29T09:50:08.914351Z",
          "recordTime":"2026-07-29T09:50:08.914351Z",
          "events":[{"CreatedEvent":{"offset":9728,"nodeId":0,"contractId":"00d2",
                     "templateId":"pkg:RichTypes:Marker","createArgument":{"owner":"alice::1220ab"},
                     "observers":[],"signatories":[]}}]}}}}
        """;

    [Fact]
    public void GetActiveContractsResponse_reads_the_contractEntry_wrapper_without_a_value_level()
    {
        var response = JsonSerializer.Deserialize<GetActiveContractsResponse>(
            ActiveContractEntry, RestRefitSettings.SerializerOptions);

        response.Should().NotBeNull();
        response.ContractEntry.Should().NotBeNull();
        response.ContractEntry.JsActiveContract.Should().NotBeNull();
        response.ContractEntry.JsActiveContract.CreatedEvent.Offset.Should().Be("31");
        response.ContractEntry.JsActiveContract.ReassignmentCounter.Should().Be("0");
    }

    [Fact]
    public void GetUpdatesResponse_reads_a_transaction_arm_through_its_value_level()
    {
        var response = JsonSerializer.Deserialize<GetUpdatesResponse>(
            TransactionUpdate, RestRefitSettings.SerializerOptions);

        response.Should().NotBeNull();
        response.Update.Should().NotBeNull();
        response.Update.Transaction.Should().NotBeNull();
        response.Update.Transaction.Offset.Should().Be("9728");
        response.Update.Transaction.Events.Should().ContainSingle();
        response.Update.Transaction.Events.Single().CreatedEvent.Should().NotBeNull();
    }

    [Fact]
    public void GetUpdateResponse_reads_a_transaction_arm_through_its_value_level()
    {
        var response = JsonSerializer.Deserialize<GetUpdateResponse>(
            TransactionUpdate, RestRefitSettings.SerializerOptions);

        response.Should().NotBeNull();
        response.Update.Should().NotBeNull();
        response.Update.Transaction.Should().NotBeNull();
        response.Update.Transaction.Offset.Should().Be("9728");
    }

    [Fact]
    public void CreatedEvent_reads_the_singular_createArgument_the_server_sends()
    {
        var response = JsonSerializer.Deserialize<GetActiveContractsResponse>(
            ActiveContractEntry, RestRefitSettings.SerializerOptions);

        response.Should().NotBeNull();
        var payload = response.ContractEntry.JsActiveContract.CreatedEvent.CreateArgument;
        payload.Should().NotBeNull();
        payload.Fields.Should().BeNullOrEmpty();
        payload.AdditionalProperties.Should().ContainKey("owner");
    }

    [Fact]
    public void GetUpdatesResponse_rejects_a_transaction_arm_that_arrives_without_its_value_level()
    {
        var act = () => JsonSerializer.Deserialize<GetUpdatesResponse>(
            """{"update":{"Transaction":{"updateId":"1220ef","offset":9728}}}""",
            RestRefitSettings.SerializerOptions);

        act.Should().Throw<JsonException>().WithMessage("*Transaction*value*");
    }

    [Fact]
    public void CompletionStreamResponse_reads_a_completion_arm_through_its_value_level()
    {
        var response = JsonSerializer.Deserialize<CompletionStreamResponse>(
            """{"completionResponse":{"Completion":{"value":{"offset":9706,"commandId":"c-1"}}}}""",
            RestRefitSettings.SerializerOptions);

        response.Should().NotBeNull();
        response.CompletionResponse.Completion.Offset.Should().Be("9706");
    }

    [Fact]
    public void TopologyEvent_reads_an_authorization_arm_through_its_value_level()
    {
        var topologyEvent = JsonSerializer.Deserialize<TopologyEvent>(
            """{"event":{"ParticipantAuthorizationAdded":{"value":{"partyId":"alice::1220ab","participantId":"p-1"}}}}""",
            RestRefitSettings.SerializerOptions);

        topologyEvent.Should().NotBeNull();
        topologyEvent.Event.Should().NotBeNull();
        topologyEvent.Event.ParticipantAuthorizationAdded.PartyId.Should().Be("alice::1220ab");
    }
}
