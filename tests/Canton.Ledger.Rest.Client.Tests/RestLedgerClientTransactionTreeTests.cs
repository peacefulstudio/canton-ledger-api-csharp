// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestLedgerClientTransactionTreeTests : IDisposable
{
    private static readonly Party Alice = new("party::alice");
    private static readonly SubmitterInfo AliceSubmitter = new(new HashSet<Party> { Alice }, new HashSet<Party>());

    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    private StubHttpClientFactory TrackedFactory(RecordingHttpHandler transport)
    {
        var factory = new StubHttpClientFactory(transport);
        _factories.Add(factory);
        return factory;
    }

    private sealed record TestTemplate : ITemplate
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "Template");
        public static string PackageId => "pkg";
        public static string PackageName => "pkg-name";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, [new DamlField("owner", Alice.ToDamlValue())]);
    }

    private RestLedgerClient ClientWith(RecordingHttpHandler transport) =>
        new(TrackedFactory(transport), Options.Create(new RestLedgerClientOptions
        {
            HttpAddress = "http://localhost:7575",
            UserId = "test-user",
        }));

    private static CommandsSubmission Submission() =>
        CommandsSubmission.Single(CreateCommand.For(new TestTemplate()))
            .WithCommandId(new CommandId("cmd-1"));

    [Fact]
    public async Task TrySubmitAndWaitForTransactionTreeAsync_requests_the_ledger_effects_shape()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, SubmitResponse(TreeShapedEvents));
        var client = ClientWith(transport);

        await client.TrySubmitAndWaitForTransactionTreeAsync(
            Submission(), AliceSubmitter, cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/commands/submit-and-wait-for-transaction");
        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("transactionFormat").GetProperty("transactionShape")
            .GetString().Should().Be("TRANSACTION_SHAPE_LEDGER_EFFECTS");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_still_leaves_the_transaction_format_unset()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, SubmitResponse(TreeShapedEvents));
        var client = ClientWith(transport);

        await client.TrySubmitAndWaitForTransactionAsync(
            Submission().WithActAs(Alice), cancellationToken: TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.TryGetProperty("transactionFormat", out _).Should().BeFalse();
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_still_returns_the_flattened_shape_for_a_nested_transaction()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, SubmitResponse(TreeShapedEvents));
        var client = ClientWith(transport);

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            Submission().WithActAs(Alice), cancellationToken: TestContext.Current.CancellationToken);

        var flat = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>().Subject.Result;
        flat.CreatedContracts.Select(c => c.ContractId).Should().Equal("00child", "00sibling");
        flat.ExercisedEvents.Select(e => e.ChoiceName).Should().Equal("ExecuteSwap");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionTreeAsync_returns_the_hierarchy_of_the_committed_transaction()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, SubmitResponse(TreeShapedEvents));
        var client = ClientWith(transport);

        var outcome = await client.TrySubmitAndWaitForTransactionTreeAsync(
            Submission(), AliceSubmitter, cancellationToken: TestContext.Current.CancellationToken);

        var tree = outcome.Should().BeOfType<ExerciseOutcome<TransactionTree>.One>().Subject.Result;
        tree.UpdateId.Should().Be("upd-1");
        tree.CompletionOffset.Value.Should().Be(7L);
        tree.RootEvents.Should().HaveCount(2);
        var swap = tree.RootEvents[0].Should().BeOfType<TreeEvent.Exercised>().Subject;
        swap.ChoiceName.Should().Be("ExecuteSwap");
        swap.ChildEvents.Should().ContainSingle().Which.Should().BeOfType<TreeEvent.Created>()
            .Which.ContractId.Should().Be("00child");
        tree.RootEvents[1].Should().BeOfType<TreeEvent.Created>().Which.ContractId.Should().Be("00sibling");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionTreeAsync_returns_InfraError_when_the_node_ids_cannot_form_a_tree()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            SubmitResponse($"{CreatedJson(3, "00late")}, {CreatedJson(1, "00early")}"));
        var client = ClientWith(transport);

        var outcome = await client.TrySubmitAndWaitForTransactionTreeAsync(
            Submission(), AliceSubmitter, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionTree>.InfraError>()
            .Which.Message.Should().Contain("node ids must strictly ascend");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionTreeAsync_returns_a_DamlError_outcome_when_the_participant_rejects_the_command()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.Conflict,
            """
            {
              "code": 9,
              "message": "DUPLICATE_COMMAND",
              "details": [
                {
                  "@type": "type.googleapis.com/google.rpc.ErrorInfo",
                  "reason": "DUPLICATE_COMMAND",
                  "metadata": {"category": "ContentionOnSharedResources"}
                }
              ]
            }
            """);
        var client = ClientWith(transport);

        var outcome = await client.TrySubmitAndWaitForTransactionTreeAsync(
            Submission(), AliceSubmitter, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionTree>.DamlError>()
            .Which.ErrorId.Should().Be("DUPLICATE_COMMAND");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionTreeAsync_returns_InfraError_when_the_response_carries_no_transaction()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{}");
        var client = ClientWith(transport);

        var outcome = await client.TrySubmitAndWaitForTransactionTreeAsync(
            Submission(), AliceSubmitter, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionTree>.InfraError>()
            .Which.Message.Should().Contain("no transaction was present");
    }

    [Fact]
    public async Task GetUpdateTreeByOffsetAsync_projects_the_hierarchy_of_the_transaction_at_the_offset()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, PointReadResponse(TreeShapedEvents));
        var client = ClientWith(transport);

        var tree = await client.GetUpdateTreeByOffsetAsync(7L, AliceSubmitter, TestContext.Current.CancellationToken);

        tree.UpdateId.Should().Be("upd-1");
        tree.RootEvents[0].Should().BeOfType<TreeEvent.Exercised>()
            .Which.ChildEvents.Should().ContainSingle().Which.Should().BeOfType<TreeEvent.Created>()
            .Which.ContractId.Should().Be("00child");

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/updates/update-by-offset");
        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("offset").GetString().Should().Be("7");
        body.RootElement.GetProperty("updateFormat").GetProperty("includeTransactions")
            .GetProperty("transactionShape").GetString().Should().Be("TRANSACTION_SHAPE_LEDGER_EFFECTS");
    }

    [Fact]
    public async Task GetUpdateTreeByOffsetAsync_throws_when_the_node_ids_cannot_form_a_tree()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            PointReadResponse($"{CreatedJson(3, "00late")}, {CreatedJson(1, "00early")}"));
        var client = ClientWith(transport);

        var act = () => client.GetUpdateTreeByOffsetAsync(7L, AliceSubmitter, TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Contain("node ids must strictly ascend");
        thrown.Which.InnerException.Should().BeOfType<MalformedTransactionTreeException>();
    }

    [Fact]
    public async Task GetUpdateTreeByOffsetAsync_throws_when_the_update_is_not_a_transaction()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, """{"update": {"Reassignment": {"value": {"offset": "7", "events": []}}}}""");
        var client = ClientWith(transport);

        var act = () => client.GetUpdateTreeByOffsetAsync(7L, AliceSubmitter, TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Contain("Reassignment");
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task GetUpdateTreeByOffsetAsync_throws_ArgumentOutOfRangeException_for_a_non_positive_offset(long offset)
    {
        var client = ClientWith(new RecordingHttpHandler());

        var act = () => client.GetUpdateTreeByOffsetAsync(offset, AliceSubmitter, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetUpdateByOffsetAsync_still_returns_the_flattened_shape_for_a_nested_transaction()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, PointReadResponse(TreeShapedEvents));
        var client = ClientWith(transport);

        var result = await client.GetUpdateByOffsetAsync(7L, AliceSubmitter, TestContext.Current.CancellationToken);

        result.CreatedContracts.Select(c => c.ContractId).Should().Equal("00child", "00sibling");
        result.ExercisedEvents.Select(e => e.ChoiceName).Should().Equal("ExecuteSwap");
    }

    private const string TemplateIdJson =
        """{"packageId": "pkg", "moduleName": "Module", "entityName": "Template"}""";

    private static string CreatedJson(int nodeId, string contractId) =>
        $$$"""
        {
          "CreatedEvent": {
            "offset": "7",
            "nodeId": {{{nodeId}}},
            "contractId": "{{{contractId}}}",
            "templateId": {{{TemplateIdJson}}},
            "createArgument": {"fields": [{"label": "owner", "value": {"party": "party::alice"}}]}
          }
        }
        """;

    private static readonly string TreeShapedEvents =
        $$"""
        {
          "ExercisedEvent": {
            "offset": "7",
            "nodeId": 0,
            "lastDescendantNodeId": 1,
            "contractId": "00target",
            "templateId": {{TemplateIdJson}},
            "choice": "ExecuteSwap",
            "consuming": false,
            "actingParties": ["party::alice"],
            "witnessParties": ["party::alice"]
          }
        },
        {{CreatedJson(1, "00child")}},
        {{CreatedJson(2, "00sibling")}}
        """;

    private static string SubmitResponse(string eventsJson) =>
        $$"""
        {
          "transaction": {
            "updateId": "upd-1",
            "commandId": "cmd-1",
            "offset": "7",
            "events": [{{eventsJson}}]
          }
        }
        """;

    private static string PointReadResponse(string eventsJson) =>
        $$"""
        {
          "update": {
            "Transaction": {
              "value": {
                "updateId": "upd-1",
                "commandId": "cmd-1",
                "offset": "7",
                "events": [{{eventsJson}}]
              }
            }
          }
        }
        """;
}
