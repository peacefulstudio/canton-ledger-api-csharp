// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing.Helpers;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestLedgerClientCantonTests : IDisposable
{
    private static readonly Party Alice = new("party::alice");
    private static readonly RuntimeCommands.SubmitterInfo AliceSubmitter =
        new(new HashSet<Party> { Alice }, new HashSet<Party>());

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

    private RestLedgerClient ClientWith(RecordingHttpHandler transport, string? userId = null) =>
        new(TrackedFactory(transport), Options.Create(new RestLedgerClientOptions
        {
            HttpAddress = "http://localhost:7575",
            UserId = userId,
        }));

    private const string ViewedInterfaceIdJson =
        """{"packageId": "viewed-pkg", "moduleName": "Token.Api", "entityName": "IViewedHolding"}""";

    private static RecordingHttpHandler SnapshotTransport(string interfaceViewJson) =>
        new RecordingHttpHandler()
            .WithResponseForPath("/v2/state/ledger-end", HttpStatusCode.OK, """{"offset": 9}""")
            .WithResponseForPath(
                "/v2/state/active-contracts",
                HttpStatusCode.OK,
                $$$"""
                [{
                  "contractEntry": {
                    "JsActiveContract": {
                      "createdEvent": {
                        "offset": "9",
                        "contractId": "00impl",
                        "templateId": {"packageId": "impl-pkg", "moduleName": "Token.Impl", "entityName": "Asset"},
                        "createArgument": {"fields": [{"label": "amount", "value": {"numeric": "999"}}]},
                        "interfaceViews": [{{{interfaceViewJson}}}],
                        "witnessParties": ["party::alice"]
                      },
                      "synchronizerId": "sync-1",
                      "reassignmentCounter": "0"
                    }
                  }
                }]
                """);

    [Fact]
    public async Task QueryActiveAsync_decodes_the_participant_computed_interface_view_into_the_view_record()
    {
        ICantonLedgerClient client = ClientWith(SnapshotTransport(
            $$$"""
            {
              "interfaceId": {{{ViewedInterfaceIdJson}}},
              "viewStatus": {"code": 0, "message": ""},
              "viewValue": {"fields": [{"label": "amount", "value": {"numeric": "42.5"}}]}
            }
            """));

        var holdings = await client.QueryActiveAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            AliceSubmitter, cancellationToken: TestContext.Current.CancellationToken);

        holdings.Should().ContainSingle();
        holdings[0].Id.Value.Should().Be("00impl");
        holdings[0].View.Amount.Should().Be(42.5m);
    }

    [Fact]
    public async Task QueryActiveAsync_throws_LedgerOperationException_when_the_view_status_is_not_Ok()
    {
        ICantonLedgerClient client = ClientWith(SnapshotTransport(
            $$$"""
            {
              "interfaceId": {{{ViewedInterfaceIdJson}}},
              "viewStatus": {"code": 2, "message": "view computation failed"}
            }
            """));

        var querying = async () => await client.QueryActiveAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            AliceSubmitter, cancellationToken: TestContext.Current.CancellationToken);

        (await querying.Should().ThrowAsync<LedgerOperationException>())
            .Which.Message.Should().Contain(nameof(UnclassifiedKind.InterfaceViewUnavailable));
    }

    [Fact]
    public async Task GetLedgerApiVersionAsync_binds_the_version_from_v2_version()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, """{"version":"3.5.9"}""");
        var client = ClientWith(transport);

        var version = await client.GetLedgerApiVersionAsync(TestContext.Current.CancellationToken);

        version.Should().Be("3.5.9");
        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/version");
    }

    [Fact]
    public async Task GetLedgerApiVersionAsync_throws_a_LedgerOperationException_on_a_non_success_response()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.ServiceUnavailable, "{}");
        var client = ClientWith(transport);

        var act = () => client.GetLedgerApiVersionAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
    }

    [Fact]
    public async Task GetConnectedSynchronizersAsync_maps_each_synchronizer_and_its_permission()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """
            {
              "connectedSynchronizers": [
                {"synchronizerAlias": "sync-a", "synchronizerId": "sync-a::id", "permission": "PARTICIPANT_PERMISSION_SUBMISSION"},
                {"synchronizerAlias": "sync-b", "synchronizerId": "sync-b::id", "permission": "PARTICIPANT_PERMISSION_OBSERVATION"}
              ]
            }
            """);
        var client = ClientWith(transport);

        var synchronizers = await client.GetConnectedSynchronizersAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        synchronizers.Should().SatisfyRespectively(
            first =>
            {
                first.SynchronizerAlias.Should().Be("sync-a");
                first.SynchronizerId.Should().Be("sync-a::id");
                first.Permission.Should().Be(SynchronizerPermissionLevel.Submission);
            },
            second =>
            {
                second.SynchronizerAlias.Should().Be("sync-b");
                second.Permission.Should().Be(SynchronizerPermissionLevel.Observation);
            });
        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/state/connected-synchronizers");
    }

    [Fact]
    public async Task GetConnectedSynchronizersAsync_scopes_the_query_by_party_and_participant_id()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, """{"connectedSynchronizers": []}""");
        var client = ClientWith(transport);

        await client.GetConnectedSynchronizersAsync(
            Alice, "participant-1", TestContext.Current.CancellationToken);

        var query = transport.LastRequest!.RequestUri!.Query;
        query.Should().Contain("party=party%3A%3Aalice").And.Contain("participantId=participant-1");
    }

    [Fact]
    public async Task GetConnectedSynchronizersAsync_throws_JsonException_for_a_permission_value_outside_the_vendored_enum()
    {
        // The wire permission is a string enum (JsonStringEnumConverter has no fallback), so a
        // value the vendored spec doesn't know about fails deserialization rather than degrading
        // to SynchronizerPermissionLevel.Unrecognized -- unlike the retired int-ordinal encoding,
        // where any out-of-range number bound successfully and MapPermission's default case
        // caught it.
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """{"connectedSynchronizers": [{"synchronizerAlias": "a", "synchronizerId": "a::id", "permission": "PARTICIPANT_PERMISSION_SOME_FUTURE_VALUE"}]}""");
        var client = ClientWith(transport);

        var act = () => client.GetConnectedSynchronizersAsync(cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<JsonException>();
    }

    [Fact]
    public async Task GetUpdateByOffsetAsync_projects_the_transaction_at_the_offset()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, PointReadTransactionBody);
        var client = ClientWith(transport);

        var result = await client.GetUpdateByOffsetAsync(7L, AliceSubmitter, TestContext.Current.CancellationToken);

        result.UpdateId.Should().Be("upd-1");
        result.CompletionOffset.Value.Should().Be(7L);
        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/updates/update-by-offset");
        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("offset").GetString().Should().Be("7");
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task GetUpdateByOffsetAsync_throws_ArgumentOutOfRangeException_for_a_non_positive_offset(long offset)
    {
        var client = ClientWith(new RecordingHttpHandler());

        var act = () => client.GetUpdateByOffsetAsync(offset, AliceSubmitter, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetUpdateByOffsetAsync_throws_InvalidOperationException_when_the_update_is_not_a_transaction()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, """{"update": {"Reassignment": {"value": {"offset": "7", "events": []}}}}""");
        var client = ClientWith(transport);

        var act = () => client.GetUpdateByOffsetAsync(7L, AliceSubmitter, TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Contain("Reassignment");
    }

    [Fact]
    public async Task GetUpdateByIdAsync_projects_the_transaction_by_id()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, PointReadTransactionBody);
        var client = ClientWith(transport);

        var result = await client.GetUpdateByIdAsync("upd-1", AliceSubmitter, TestContext.Current.CancellationToken);

        result.UpdateId.Should().Be("upd-1");
        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/updates/update-by-id");
        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("updateId").GetString().Should().Be("upd-1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetUpdateByIdAsync_throws_ArgumentException_for_a_blank_update_id(string updateId)
    {
        var client = ClientWith(new RecordingHttpHandler());

        var act = () => client.GetUpdateByIdAsync(updateId, AliceSubmitter, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SubmitAsync_fires_the_commands_and_returns_the_effective_command_id()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{}");
        var client = ClientWith(transport, userId: "test-user");
        var submission = RuntimeCommands.CommandsSubmission.Single(RuntimeCommands.CreateCommand.For(new TestTemplate()))
            .WithActAs(Alice)
            .WithCommandId(new RuntimeCommands.CommandId("cmd-fire"));

        var commandId = await client.SubmitAsync(submission, TestContext.Current.CancellationToken);

        commandId.Value.Should().Be("cmd-fire");
        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/commands/async/submit");
        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("commandId").GetString().Should().Be("cmd-fire");
        body.RootElement.GetProperty("userId").GetString().Should().Be("test-user");
    }

    [Fact]
    public async Task SubmitAsync_mints_a_command_id_when_the_submission_omits_one()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{}");
        var client = ClientWith(transport);
        var submission = RuntimeCommands.CommandsSubmission.Single(RuntimeCommands.CreateCommand.For(new TestTemplate()))
            .WithActAs(Alice);

        var commandId = await client.SubmitAsync(submission, TestContext.Current.CancellationToken);

        commandId.Value.Should().NotBeNullOrWhiteSpace();
        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("commandId").GetString().Should().Be(commandId.Value);
    }

    [Fact]
    public async Task SubmitAsync_throws_a_LedgerOperationException_on_a_non_success_response()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.BadRequest,
            """{"code": 3, "message": "invalid", "details": [{"@type": "type.googleapis.com/google.rpc.ErrorInfo", "reason": "INVALID_ARGUMENT", "metadata": {}}]}""");
        var client = ClientWith(transport);
        var submission = RuntimeCommands.CommandsSubmission.Single(RuntimeCommands.CreateCommand.For(new TestTemplate()))
            .WithActAs(Alice);

        var act = () => client.SubmitAsync(submission, TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.ErrorId.Should().Be("INVALID_ARGUMENT");
    }

    [Fact]
    public async Task SubmitReassignmentAsync_fires_the_reassignment_and_returns_the_effective_command_id()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{}");
        var client = ClientWith(transport, userId: "test-user");
        var submission = ReassignmentSubmission
            .Of(new UnassignCommand("00cid", new SynchronizerId("sync-a"), new SynchronizerId("sync-b")), Alice)
            .WithCommandId(new RuntimeCommands.CommandId("cmd-reassign"));

        var commandId = await client.SubmitReassignmentAsync(submission, TestContext.Current.CancellationToken);

        commandId.Value.Should().Be("cmd-reassign");
        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/commands/async/submit-reassignment");
        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        var commands = body.RootElement.GetProperty("reassignmentCommands");
        commands.GetProperty("submitter").GetString().Should().Be("party::alice");
        commands.GetProperty("commands")[0].GetProperty("command").GetProperty("UnassignCommand")
            .GetProperty("value").GetProperty("contractId").GetString().Should().Be("00cid");
    }

    [Fact]
    public async Task SubmitReassignmentAsync_throws_a_LedgerOperationException_on_a_non_success_response()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.BadRequest,
            """{"code": 3, "message": "invalid", "details": [{"@type": "type.googleapis.com/google.rpc.ErrorInfo", "reason": "INVALID_ARGUMENT", "metadata": {}}]}""");
        var client = ClientWith(transport);
        var submission = ReassignmentSubmission
            .Of(new UnassignCommand("00cid", new SynchronizerId("sync-a"), new SynchronizerId("sync-b")), Alice);

        var act = () => client.SubmitReassignmentAsync(submission, TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.ErrorId.Should().Be("INVALID_ARGUMENT");
    }

    [Fact]
    public async Task TrySubmitAndWaitForReassignmentAsync_projects_the_unassigned_event()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """
            {
              "reassignment": {
                "updateId": "upd-1",
                "offset": "5",
                "events": [
                  {
                    "JsUnassignedEvent": {
                      "value": {
                        "offset": "5",
                        "reassignmentId": "reassign-1",
                        "reassignmentCounter": "1",
                        "contractId": "00cid",
                        "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "Template"},
                        "source": "sync-a",
                        "target": "sync-b",
                        "witnessParties": ["party::alice"]
                      }
                    }
                  }
                ]
              }
            }
            """);
        var client = ClientWith(transport);
        var submission = ReassignmentSubmission.Of(
            new UnassignCommand("00cid", new SynchronizerId("sync-a"), new SynchronizerId("sync-b")), Alice);

        var outcome = await client.TrySubmitAndWaitForReassignmentAsync<TestTemplate>(
            submission, cancellationToken: TestContext.Current.CancellationToken);

        var one = outcome.Should().BeOfType<ExerciseOutcome<ContractStreamEvent<TestTemplate>>.One>().Subject;
        var unassigned = one.Result.Should().BeOfType<ContractStreamEvent<TestTemplate>.Unassigned>().Subject;
        unassigned.ContractId.Value.Should().Be("00cid");
        unassigned.ReassignmentId.Should().Be("reassign-1");
        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/commands/submit-and-wait-for-reassignment");
    }

    [Fact]
    public async Task TrySubmitAndWaitForReassignmentAsync_returns_a_DamlError_outcome_on_a_structured_error()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.Conflict,
            """{"code": 9, "message": "rejected", "details": [{"@type": "type.googleapis.com/google.rpc.ErrorInfo", "reason": "REASSIGNMENT_REJECTED", "metadata": {}}]}""");
        var client = ClientWith(transport);
        var submission = ReassignmentSubmission.Of(
            new UnassignCommand("00cid", new SynchronizerId("sync-a"), new SynchronizerId("sync-b")), Alice);

        var outcome = await client.TrySubmitAndWaitForReassignmentAsync<TestTemplate>(
            submission, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractStreamEvent<TestTemplate>>.DamlError>()
            .Which.ErrorId.Should().Be("REASSIGNMENT_REJECTED");
    }

    [Fact]
    public async Task TrySubmitAndWaitForReassignmentAsync_returns_an_InfraError_outcome_when_the_transport_fails()
    {
        var transport = new RecordingHttpHandler().WithTransportException(new HttpRequestException("connection refused"));
        var client = ClientWith(transport);
        var submission = ReassignmentSubmission.Of(
            new UnassignCommand("00cid", new SynchronizerId("sync-a"), new SynchronizerId("sync-b")), Alice);

        var outcome = await client.TrySubmitAndWaitForReassignmentAsync<TestTemplate>(
            submission, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractStreamEvent<TestTemplate>>.InfraError>()
            .Which.Message.Should().Contain("connection refused");
    }

    private const string PointReadTransactionBody =
        """
        {
          "update": {
            "Transaction": {
              "value": {
                "updateId": "upd-1",
                "commandId": "cmd-1",
                "offset": "7",
                "events": [
                  {
                    "CreatedEvent": {
                      "offset": "7",
                      "contractId": "00holding",
                      "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "Template"},
                      "createArgument": {"fields": [{"label": "owner", "value": {"party": "party::alice"}}]}
                    }
                  }
                ]
              }
            }
          }
        }
        """;
}
