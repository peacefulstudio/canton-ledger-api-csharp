// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestLedgerClientStreamingTests : IDisposable
{
    private static readonly Party Alice = new("party::alice");

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
        new(TrackedFactory(transport));

    [Fact]
    public void SupportsUnboundedStreaming_is_false()
    {
        var client = ClientWith(new RecordingHttpHandler());

        client.SupportsUnboundedStreaming.Should().BeFalse();
    }

    [Fact]
    public async Task SubscribeActiveAsync_posts_to_v2_state_active_contracts_and_ends_with_a_checkpoint()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """
            [{"contractEntry": {"JsActiveContract": {"createdEvent": {"offset": "10", "contractId": "00holding", "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "Template"}, "createArgument": {"fields": []}, "witnessParties": ["party::alice"]}, "synchronizerId": "sync-1"}}}]
            """);
        var client = ClientWith(transport);

        var entries = new List<AcsSnapshotEntry<TestTemplate>>();
        await foreach (var entry in client.SubscribeActiveAsync<TestTemplate>(
            Alice, LedgerOffset.At(10), TestContext.Current.CancellationToken))
        {
            entries.Add(entry);
        }

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/state/active-contracts");
        entries.Should().HaveCount(2);
        var created = entries[0].Should().BeOfType<AcsSnapshotEntry<TestTemplate>.Created>().Subject;
        created.ContractId.Value.Should().Be("00holding");
        var checkpoint = entries[1].Should().BeOfType<AcsSnapshotEntry<TestTemplate>.Checkpoint>().Subject;
        checkpoint.Resume.Offset.Value.Should().Be(10L);
    }

    [Fact]
    public async Task SubscribeActiveAsync_surfaces_the_unassignment_of_an_incomplete_unassigned_entry()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """
            [{"contractEntry": {"JsIncompleteUnassigned": {"createdEvent": {"offset": "10", "contractId": "00holding", "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "Template"}, "createArgument": {"fields": []}, "witnessParties": ["party::alice"]}, "unassignedEvent": {"contractId": "00holding", "source": "sync-1", "target": "sync-2", "offset": "11", "reassignmentId": "reassignment-1", "reassignmentCounter": "7"}}}}]
            """);
        var client = ClientWith(transport);

        var entries = new List<AcsSnapshotEntry<TestTemplate>>();
        await foreach (var entry in client.SubscribeActiveAsync<TestTemplate>(
            Alice, LedgerOffset.At(11), TestContext.Current.CancellationToken))
        {
            entries.Add(entry);
        }

        entries.Should().HaveCount(3);
        entries[0].Should().BeOfType<AcsSnapshotEntry<TestTemplate>.Created>();
        var unassigned = entries[1].Should().BeOfType<AcsSnapshotEntry<TestTemplate>.Unclassified>().Subject;
        unassigned.Offset.Value.Should().Be(11L);
        unassigned.Kind.Should().Be(UnclassifiedKind.UnassignedEvent.ToString());
        entries[2].Should().BeOfType<AcsSnapshotEntry<TestTemplate>.Checkpoint>();
    }

    [Fact]
    public async Task SubscribeActiveAsync_reports_an_entry_without_a_created_event_at_the_snapshot_offset()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """
            [{"contractEntry": {"JsActiveContract": {"synchronizerId": "sync-1"}}}]
            """);
        var client = ClientWith(transport);

        var entries = new List<AcsSnapshotEntry<TestTemplate>>();
        await foreach (var entry in client.SubscribeActiveAsync<TestTemplate>(
            Alice, LedgerOffset.At(42), TestContext.Current.CancellationToken))
        {
            entries.Add(entry);
        }

        entries.Should().HaveCount(2);
        var unclassified = entries[0].Should().BeOfType<AcsSnapshotEntry<TestTemplate>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(42L);
        entries[1].Should().BeOfType<AcsSnapshotEntry<TestTemplate>.Checkpoint>()
            .Which.Resume.Offset.Value.Should().Be(42L);
    }

    [Fact]
    public async Task SubscribeActiveAsync_reports_an_unparseable_created_offset_at_the_resolved_ledger_end()
    {
        var transport = new RecordingHttpHandler()
            .WithResponse(
                HttpStatusCode.OK,
                """
                [{"contractEntry": {"JsActiveContract": {"createdEvent": {"offset": "not-a-number", "contractId": "00holding", "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "Template"}, "createArgument": {"fields": []}}, "synchronizerId": "sync-1"}}}]
                """)
            .WithResponseForPath("/v2/state/ledger-end", HttpStatusCode.OK, """{"offset": 5}""");
        var client = ClientWith(transport);

        var entries = new List<AcsSnapshotEntry<TestTemplate>>();
        await foreach (var entry in client.SubscribeActiveAsync<TestTemplate>(
            Alice, cancellationToken: TestContext.Current.CancellationToken))
        {
            entries.Add(entry);
        }

        entries.Should().HaveCount(2);
        var unclassified = entries[0].Should().BeOfType<AcsSnapshotEntry<TestTemplate>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(5L);
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure.ToString());
    }

    [Fact]
    public async Task SubscribeActiveAsync_resolves_the_ledger_end_when_activeAtOffset_is_null()
    {
        var transport = new RecordingHttpHandler()
            .WithResponse(HttpStatusCode.OK, "[]")
            .WithResponseForPath("/v2/state/ledger-end", HttpStatusCode.OK, """{"offset": 5}""");
        var client = ClientWith(transport);

        var entries = new List<AcsSnapshotEntry<TestTemplate>>();
        await foreach (var entry in client.SubscribeActiveAsync<TestTemplate>(
            Alice, cancellationToken: TestContext.Current.CancellationToken))
        {
            entries.Add(entry);
        }

        // Ledger-end resolution is a GET before the ACS POST; the recorder only keeps the last
        // request, which must be the bounded snapshot call, scoped to the resolved ledger end.
        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/state/active-contracts");
        transport.LastRequestBody.Should().Contain("\"activeAtOffset\":\"5\"");
        entries.OfType<AcsSnapshotEntry<TestTemplate>.Checkpoint>().Should().ContainSingle()
            .Which.Resume.Offset.Value.Should().Be(5L);
    }

    [Fact]
    public async Task SubscribeActiveAsync_throws_LedgerResultTooLargeException_on_413()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.RequestEntityTooLarge, """{"message": "too many results"}""");
        var client = ClientWith(transport);

        var act = async () =>
        {
            await foreach (var _ in client.SubscribeActiveAsync<TestTemplate>(
                Alice, LedgerOffset.At(1), TestContext.Current.CancellationToken))
            {
            }
        };

        await act.Should().ThrowAsync<LedgerResultTooLargeException>();
    }

    [Fact]
    public async Task SubscribeActiveAsync_throws_LedgerOperationException_on_a_structured_error_response()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.BadRequest,
            """
            {
              "code": 3,
              "message": "invalid argument",
              "details": [
                {"@type": "type.googleapis.com/google.rpc.ErrorInfo", "reason": "INVALID_ARGUMENT", "metadata": {}}
              ]
            }
            """);
        var client = ClientWith(transport);

        var act = async () =>
        {
            await foreach (var _ in client.SubscribeActiveAsync<TestTemplate>(
                Alice, LedgerOffset.At(1), TestContext.Current.CancellationToken))
            {
            }
        };

        var thrown = await act.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.ErrorId.Should().Be("INVALID_ARGUMENT");
    }

    [Fact]
    public async Task SubscribeAsync_posts_to_v2_updates_with_begin_exclusive_and_end_inclusive()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """
            [{"update": {"Transaction": {"value": {"offset": "11", "synchronizerId": "sync-1", "events": [{"CreatedEvent": {"offset": "11", "contractId": "00holding", "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "Template"}, "createArgument": {"fields": []}, "witnessParties": ["party::alice"]}}]}}}},
            {"update": {"OffsetCheckpoint": {"value": {"offset": "11"}}}}]
            """);
        var client = ClientWith(transport);

        var events = new List<ContractStreamEvent<TestTemplate>>();
        await foreach (var evt in client.SubscribeAsync<TestTemplate>(
            Alice, LedgerOffset.At(5), LedgerOffset.At(11), TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/updates");
        events.Should().HaveCount(2);
        events[0].Should().BeOfType<ContractStreamEvent<TestTemplate>.Created>();
        var checkpoint = events[1].Should().BeOfType<ContractStreamEvent<TestTemplate>.Checkpoint>().Subject;
        checkpoint.Offset.Value.Should().Be(11L);
    }

    [Fact]
    public void SubscribeAsync_throws_NotSupportedException_pointing_at_the_websocket_follow_up_when_toOffset_is_null()
    {
        var client = ClientWith(new RecordingHttpHandler());

        var act = () => client.SubscribeAsync<TestTemplate>(Alice, LedgerOffset.At(0), toOffset: null);

        act.Should().Throw<NotSupportedException>().WithMessage("*WebSocket*");
    }

    [Fact]
    public void SubscribeLedgerEffectsAsync_throws_NotSupportedException_pointing_at_the_websocket_follow_up_when_toOffset_is_null()
    {
        var client = ClientWith(new RecordingHttpHandler());

        var act = () => client.SubscribeLedgerEffectsAsync<TestTemplate>(Alice, LedgerOffset.At(0), toOffset: null);

        act.Should().Throw<NotSupportedException>().WithMessage("*WebSocket*");
    }

    [Fact]
    public async Task SubscribeLedgerEffectsAsync_posts_to_v2_updates_and_projects_ledger_effects_events()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """
            [{"update": {"Transaction": {"value": {"offset": "12", "synchronizerId": "sync-1", "events": [{"ExercisedEvent": {"offset": "12", "contractId": "00holding", "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "Template"}, "choice": "Archive", "choiceArgument": {"record": {"fields": []}}, "actingParties": ["party::alice"], "consuming": true, "witnessParties": ["party::alice"], "exerciseResult": {"unit": {}}}}]}}}}]
            """);
        var client = ClientWith(transport);

        var events = new List<ContractStreamEvent<TestTemplate>>();
        await foreach (var evt in client.SubscribeLedgerEffectsAsync<TestTemplate>(
            Alice, toOffset: LedgerOffset.At(12), cancellationToken: TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }

        var exercised = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<TestTemplate>.Exercised>().Subject;
        exercised.ChoiceName.Should().Be("Archive");
        exercised.Consuming.Should().BeTrue();
    }
}
