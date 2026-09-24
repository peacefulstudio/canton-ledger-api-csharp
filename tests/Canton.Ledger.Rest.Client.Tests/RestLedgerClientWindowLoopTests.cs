// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Serialization;
using System.Text.Json;
using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing.Helpers;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestLedgerClientWindowLoopTests : IDisposable
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

    private sealed record TestTemplate : ITemplate, IDamlRecord<TestTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "WindowLoopTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "pkg-name";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, [new DamlField("owner", Alice.ToDamlValue())]);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context);

        public static TestTemplate FromRecord(DamlRecord record) => new();
    }

    private RestLedgerClient ClientWith(
        RecordingHttpHandler transport,
        long? windowLimit = null,
        TimeSpan? windowIdleTimeout = null,
        ILogger<RestLedgerClient>? logger = null)
    {
        var options = new RestLedgerClientOptions { HttpAddress = "http://localhost:7575" };
        if (windowLimit is { } limit)
        {
            options.StreamWindowLimit = limit;
        }

        if (windowIdleTimeout is { } idleTimeout)
        {
            options.StreamWindowIdleTimeout = idleTimeout;
        }

        var factory = new StubHttpClientFactory(transport);
        _factories.Add(factory);
        return new RestLedgerClient(factory, Options.Create(options), logger);
    }

    private const string OffsetPlaceholder = "OFFSET";

    private const string TransactionEntry =
        """{"update": {"Transaction": {"value": {"offset": "OFFSET", "synchronizerId": "sync-1", "events": [{"CreatedEvent": {"offset": "OFFSET", "contractId": "00holding-OFFSET", "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "WindowLoopTemplate"}, "createArgument": {}, "witnessParties": ["party::alice"]}}]}}}}""";

    private const string CheckpointEntry =
        """{"update": {"OffsetCheckpoint": {"value": {"offset": "OFFSET"}}}}""";

    private const string CompletionEntry =
        """{"completionResponse": {"Completion": {"value": {"commandId": "cmd-OFFSET", "updateId": "update-OFFSET", "offset": "OFFSET", "actAs": ["party::alice"], "status": {"code": 0}}}}}""";

    private const string CompletionCheckpointEntry =
        """{"completionResponse": {"OffsetCheckpoint": {"value": {"offset": "OFFSET"}}}}""";

    private static string TransactionWindow(params string[] offsets) => Window(TransactionEntry, offsets);

    private static string CheckpointWindow(params string[] offsets) => Window(CheckpointEntry, offsets);

    private static string CompletionWindow(params string[] offsets) => Window(CompletionEntry, offsets);

    private static string CompletionCheckpointWindow(params string[] offsets) =>
        Window(CompletionCheckpointEntry, offsets);

    private static string Window(string entry, params string[] offsets) =>
        "[" + string.Join(",", offsets.Select(
            offset => entry.Replace(OffsetPlaceholder, offset, StringComparison.Ordinal))) + "]";

    [Fact]
    public async Task SubscribeAsync_re_POSTs_the_next_window_from_the_last_observed_offset()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, TransactionWindow("11")),
            (HttpStatusCode.OK, CheckpointWindow("12")));
        var client = ClientWith(transport);
        using var cancellation = new CancellationTokenSource();

        var observed = new List<ContractStreamEvent<TestTemplate>>();
        var drain = async () =>
        {
            await foreach (var streamEvent in client.SubscribeAsync<TestTemplate>(
                Alice, LedgerOffset.At(5), toOffset: null, cancellation.Token))
            {
                observed.Add(streamEvent);
                if (observed.Count == 2)
                {
                    await cancellation.CancelAsync();
                }
            }
        };

        await drain.Should().ThrowAsync<OperationCanceledException>();
        transport.Requests.Should().HaveCount(2);
        transport.Requests[0].Body.Should().Contain("\"beginExclusive\":\"5\"");
        transport.Requests[1].Body.Should().Contain("\"beginExclusive\":\"11\"");
    }

    [Fact]
    public async Task SubscribeAsync_sends_the_window_limit_and_idle_timeout_on_every_request()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, TransactionWindow("11")),
            (HttpStatusCode.OK, TransactionWindow("12")));
        var client = ClientWith(transport);
        using var cancellation = new CancellationTokenSource();

        await DrainUntilAsync(
            client.SubscribeAsync<TestTemplate>(Alice, LedgerOffset.At(5), toOffset: null, cancellation.Token),
            cancellation,
            stopAfter: 2);

        transport.Requests.Should().HaveCount(2);
        transport.Requests.Should().OnlyContain(
            request => request.PathAndQuery == "/v2/updates?limit=200&stream_idle_timeout_ms=250");
    }

    [Fact]
    public async Task SubscribeAsync_opens_no_further_window_once_the_caller_cancels()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, TransactionWindow("11")),
            (HttpStatusCode.OK, TransactionWindow("12")));
        var client = ClientWith(transport);
        using var cancellation = new CancellationTokenSource();

        var observed = await DrainUntilAsync(
            client.SubscribeAsync<TestTemplate>(Alice, LedgerOffset.At(5), toOffset: null, cancellation.Token),
            cancellation,
            stopAfter: 1);

        observed.Should().ContainSingle();
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task SubscribeAsync_ends_with_a_terminal_StreamError_when_a_later_window_answers_413()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, TransactionWindow("11")),
            (HttpStatusCode.RequestEntityTooLarge, """{"message": "too many results"}"""));
        var client = ClientWith(transport);

        var observed = new List<ContractStreamEvent<TestTemplate>>();
        await foreach (var streamEvent in client.SubscribeAsync<TestTemplate>(
            Alice, LedgerOffset.At(5), toOffset: null, TestContext.Current.CancellationToken))
        {
            observed.Add(streamEvent);
        }

        observed.Should().HaveCount(2);
        observed[0].Should().BeOfType<ContractStreamEvent<TestTemplate>.Created>();
        var error = observed[1].Should().BeOfType<ContractStreamEvent<TestTemplate>.StreamError>().Subject;
        error.StatusCode.Should().Be((int)HttpStatusCode.RequestEntityTooLarge);
        error.Message.Should().Contain(nameof(RestLedgerClientOptions.StreamWindowLimit));
    }

    [Fact]
    public async Task SubscribeAsync_yields_nothing_for_an_empty_window_and_reopens_the_next_one_at_the_same_offset()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, "[]"),
            (HttpStatusCode.OK, TransactionWindow("7")));
        var client = ClientWith(transport);
        using var cancellation = new CancellationTokenSource();

        var observed = await DrainUntilAsync(
            client.SubscribeAsync<TestTemplate>(Alice, LedgerOffset.At(5), toOffset: null, cancellation.Token),
            cancellation,
            stopAfter: 1);

        observed.Should().ContainSingle().Which.Should().BeOfType<ContractStreamEvent<TestTemplate>.Created>();
        transport.Requests.Should().HaveCount(2);
        transport.Requests[1].Body.Should().Contain("\"beginExclusive\":\"5\"");
    }

    [Fact]
    public async Task SubscribeAsync_relays_the_participant_OffsetCheckpoint_and_resumes_from_it()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, CheckpointWindow("9")),
            (HttpStatusCode.OK, TransactionWindow("10")));
        var client = ClientWith(transport);
        using var cancellation = new CancellationTokenSource();

        var observed = await DrainUntilAsync(
            client.SubscribeAsync<TestTemplate>(Alice, LedgerOffset.At(5), toOffset: null, cancellation.Token),
            cancellation,
            stopAfter: 2);

        observed[0].Should().BeOfType<ContractStreamEvent<TestTemplate>.Checkpoint>()
            .Which.Offset.Value.Should().Be(9L);
        observed[1].Should().BeOfType<ContractStreamEvent<TestTemplate>.Created>();
        transport.Requests[1].Body.Should().Contain("\"beginExclusive\":\"9\"");
    }

    [Fact]
    public async Task SubscribeAsync_pages_a_bounded_range_wider_than_the_window_limit()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, TransactionWindow("6", "7")),
            (HttpStatusCode.OK, TransactionWindow("8")));
        var client = ClientWith(transport, windowLimit: 2);

        var observed = new List<ContractStreamEvent<TestTemplate>>();
        await foreach (var streamEvent in client.SubscribeAsync<TestTemplate>(
            Alice, LedgerOffset.At(5), LedgerOffset.At(100), TestContext.Current.CancellationToken))
        {
            observed.Add(streamEvent);
        }

        observed.Should().HaveCount(3);
        transport.Requests.Should().HaveCount(2);
        transport.Requests[1].Body.Should().Contain("\"beginExclusive\":\"7\"");
        transport.Requests.Should().OnlyContain(request => request.Body!.Contains("\"endInclusive\":\"100\""));
    }

    [Fact]
    public async Task SubscribeAsync_stops_a_bounded_range_at_the_end_offset()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, TransactionWindow("6", "7")));
        var client = ClientWith(transport, windowLimit: 2);

        var observed = new List<ContractStreamEvent<TestTemplate>>();
        await foreach (var streamEvent in client.SubscribeAsync<TestTemplate>(
            Alice, LedgerOffset.At(5), LedgerOffset.At(7), TestContext.Current.CancellationToken))
        {
            observed.Add(streamEvent);
        }

        observed.Should().HaveCount(2);
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task SubscribeAsync_ends_with_a_terminal_StreamError_when_a_window_carries_no_offset_to_resume_from()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, TransactionWindow("not-a-number"));
        var client = ClientWith(transport);

        var observed = new List<ContractStreamEvent<TestTemplate>>();
        await foreach (var streamEvent in client.SubscribeAsync<TestTemplate>(
            Alice, LedgerOffset.At(5), toOffset: null, TestContext.Current.CancellationToken))
        {
            observed.Add(streamEvent);
        }

        observed[^1].Should().BeOfType<ContractStreamEvent<TestTemplate>.StreamError>()
            .Which.Message.Should().Contain("re-read what was just delivered");
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task SubscribeAsync_follows_an_open_ended_tail_for_the_interface_typed_overload()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, TransactionWindow("11")),
            (HttpStatusCode.OK, TransactionWindow("12")));
        var client = ClientWith(transport);
        using var cancellation = new CancellationTokenSource();

        var observed = await DrainUntilAsync(
            client.SubscribeAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
                new ViewDescriptor<IViewedInterfaceMarker, ViewedInterfaceView>(),
                Alice,
                LedgerOffset.At(5),
                toOffset: null,
                cancellation.Token),
            cancellation,
            stopAfter: 2);

        observed.Should().HaveCount(2);
        transport.Requests.Should().HaveCount(2);
        transport.Requests[1].Body.Should().Contain("\"beginExclusive\":\"11\"");
    }

    private const string ActiveContractEntry =
        """{"contractEntry": {"JsActiveContract": {"createdEvent": {"offset": "3", "contractId": "OFFSET", "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "WindowLoopTemplate"}, "createArgument": {}, "witnessParties": ["party::alice"]}, "synchronizerId": "sync-1"}}}""";

    private static string ActiveContractsPage(string? nextPageToken, params string[] contractIds)
    {
        var nextPageTokenField = nextPageToken is null ? "" : $", \"nextPageToken\": \"{nextPageToken}\"";
        return "{\"activeAtOffset\": 9, \"activeContracts\": " + Window(ActiveContractEntry, contractIds) +
            nextPageTokenField + "}";
    }

    private async Task<List<AcsSnapshotEntry<TestTemplate>>> SnapshotAsync(
        RecordingHttpHandler transport, long? windowLimit = null)
    {
        var client = ClientWith(transport, windowLimit);
        var entries = new List<AcsSnapshotEntry<TestTemplate>>();
        await foreach (var entry in client.SubscribeActiveAsync<TestTemplate>(
            Alice, LedgerOffset.At(9), TestContext.Current.CancellationToken))
        {
            entries.Add(entry);
        }

        return entries;
    }

    private static string[] CreatedContractIds(IEnumerable<AcsSnapshotEntry<TestTemplate>> entries) =>
        [.. entries.OfType<AcsSnapshotEntry<TestTemplate>.Created>().Select(created => created.ContractId.Value)];

    [Fact]
    public async Task SubscribeActiveAsync_follows_the_page_token_until_a_page_carries_none()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, ActiveContractsPage("page-2", "00a", "00b")),
            (HttpStatusCode.OK, ActiveContractsPage("page-3", "00c", "00d")),
            (HttpStatusCode.OK, ActiveContractsPage(nextPageToken: null, "00e")));

        var entries = await SnapshotAsync(transport, windowLimit: 2);

        CreatedContractIds(entries).Should().Equal("00a", "00b", "00c", "00d", "00e");
        entries.Should().HaveCount(6);
        entries[^1].Should().BeOfType<AcsSnapshotEntry<TestTemplate>.Checkpoint>()
            .Which.Resume.Offset.Value.Should().Be(9L);
        transport.Requests.Should().HaveCount(3);
        transport.Requests.Select(request => request.PathAndQuery).Should().AllBe("/v2/state/active-contracts-page");
        transport.Requests[0].Body.Should().NotContain("pageToken");
        transport.Requests[1].Body.Should().Contain("\"pageToken\":\"page-2\"");
        transport.Requests[2].Body.Should().Contain("\"pageToken\":\"page-3\"");
    }

    [Fact]
    public async Task SubscribeActiveAsync_requests_every_page_at_the_same_offset_and_page_size()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, ActiveContractsPage("page-2", "00a")),
            (HttpStatusCode.OK, ActiveContractsPage(nextPageToken: null, "00b")));

        await SnapshotAsync(transport, windowLimit: 1);

        transport.Requests.Should().HaveCount(2).And.AllSatisfy(request =>
        {
            request.Body.Should().Contain("\"activeAtOffset\":\"9\"");
            request.Body.Should().Contain("\"maxPageSize\":1");
        });
    }

    [Fact]
    public async Task SubscribeActiveAsync_sends_the_default_window_limit_as_the_page_size()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, ActiveContractsPage(nextPageToken: null));

        await SnapshotAsync(transport);

        transport.Requests.Should().ContainSingle().Which.Body.Should().Contain("\"maxPageSize\":200");
    }

    [Fact]
    public async Task SubscribeActiveAsync_ends_an_empty_snapshot_with_its_checkpoint_after_one_page()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, """{"activeAtOffset": 9, "activeContracts": [], "nextPageToken": null}""");

        var entries = await SnapshotAsync(transport);

        entries.Should().ContainSingle().Which.Should().BeOfType<AcsSnapshotEntry<TestTemplate>.Checkpoint>()
            .Which.Resume.Offset.Value.Should().Be(9L);
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task SubscribeActiveAsync_reads_a_page_without_an_activeContracts_field_as_empty()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, """{"activeAtOffset": 9}""");

        var entries = await SnapshotAsync(transport);

        entries.Should().ContainSingle().Which.Should().BeOfType<AcsSnapshotEntry<TestTemplate>.Checkpoint>();
    }

    [Fact]
    public async Task SubscribeActiveAsync_treats_an_empty_page_token_as_the_last_page()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, ActiveContractsPage(nextPageToken: "", "00a")),
            (HttpStatusCode.OK, ActiveContractsPage(nextPageToken: null, "00unrequested")));

        var entries = await SnapshotAsync(transport);

        CreatedContractIds(entries).Should().Equal("00a");
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task SubscribeActiveAsync_ends_with_a_StreamError_and_no_checkpoint_when_a_later_page_fails()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, ActiveContractsPage("page-2", "00a", "00b")),
            (HttpStatusCode.BadRequest, """{"code": "INVALID_ARGUMENT", "cause": "page token expired"}"""),
            (HttpStatusCode.OK, ActiveContractsPage(nextPageToken: null, "00unrequested")));

        var entries = await SnapshotAsync(transport, windowLimit: 2);

        CreatedContractIds(entries).Should().Equal("00a", "00b");
        entries.Should().HaveCount(3);
        entries[^1].Should().BeOfType<AcsSnapshotEntry<TestTemplate>.StreamError>()
            .Which.StatusCode.Should().Be(400);
        entries.OfType<AcsSnapshotEntry<TestTemplate>.Checkpoint>().Should().BeEmpty();
        transport.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task SubscribeActiveAsync_ends_with_a_StreamError_when_a_page_body_cannot_be_decoded()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, ActiveContractsPage("page-2", "00a")),
            (HttpStatusCode.OK, "[]"));

        var entries = await SnapshotAsync(transport, windowLimit: 1);

        CreatedContractIds(entries).Should().Equal("00a");
        entries[^1].Should().BeOfType<AcsSnapshotEntry<TestTemplate>.StreamError>()
            .Which.StatusCode.Should().Be(0);
    }

    [Fact]
    public async Task SubscribeActiveAsync_at_ledger_begin_answers_the_empty_snapshot_without_a_request()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, ActiveContractsPage(nextPageToken: null, "00at-ledger-end"));
        var client = ClientWith(transport);

        var entries = new List<AcsSnapshotEntry<TestTemplate>>();
        await foreach (var entry in client.SubscribeActiveAsync<TestTemplate>(
            Alice, LedgerOffset.At(0), TestContext.Current.CancellationToken))
        {
            entries.Add(entry);
        }

        entries.Should().ContainSingle().Which.Should().BeOfType<AcsSnapshotEntry<TestTemplate>.Checkpoint>()
            .Which.Resume.Offset.Value.Should().Be(0L);
        transport.Requests.Should().BeEmpty(
            "the page endpoint reads an activeAtOffset of 0 as unset and answers at the ledger end");
    }

    [Fact]
    public async Task CompletionStreamAsync_re_POSTs_the_next_window_from_the_last_observed_offset()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, CompletionWindow("11")),
            (HttpStatusCode.OK, CompletionCheckpointWindow("12")));
        var client = ClientWith(transport);
        using var cancellation = new CancellationTokenSource();

        var observed = await DrainUntilAsync(
            client.CompletionStreamAsync(Alice, 5L, cancellation.Token), cancellation, stopAfter: 2);

        observed[0].Should().BeOfType<CompletionStreamEvent.CommandAccepted>();
        observed[1].Should().BeOfType<CompletionStreamEvent.Checkpoint>();
        transport.Requests.Should().HaveCount(2);
        transport.Requests[0].Body.Should().Contain("\"beginExclusive\":\"5\"");
        transport.Requests[1].Body.Should().Contain("\"beginExclusive\":\"11\"");
    }

    [Fact]
    public async Task CompletionStreamAsync_ends_with_a_terminal_StreamError_when_a_later_window_answers_413()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, CompletionCheckpointWindow("11")),
            (HttpStatusCode.RequestEntityTooLarge, """{"message": "too many results"}"""));
        var client = ClientWith(transport);

        var observed = new List<CompletionStreamEvent>();
        await foreach (var completionEvent in client.CompletionStreamAsync(
            Alice, 5L, TestContext.Current.CancellationToken))
        {
            observed.Add(completionEvent);
        }

        observed.Should().HaveCount(2);
        observed[0].Should().BeOfType<CompletionStreamEvent.Checkpoint>();
        var error = observed[1].Should().BeOfType<CompletionStreamEvent.StreamError>().Subject;
        error.StatusCode.Should().Be((int)HttpStatusCode.RequestEntityTooLarge);
        error.Message.Should().Contain(nameof(RestLedgerClientOptions.StreamWindowLimit));
    }

    [Fact]
    public async Task CompletionStreamAsync_ends_with_a_terminal_StreamError_when_a_window_carries_no_offset_to_resume_from()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, """[{"completionResponse": {"Empty": {"value": {}}}}]""");
        var client = ClientWith(transport);

        var observed = new List<CompletionStreamEvent>();
        await foreach (var completionEvent in client.CompletionStreamAsync(
            Alice, 5L, TestContext.Current.CancellationToken))
        {
            observed.Add(completionEvent);
        }

        observed.Should().ContainSingle()
            .Which.Should().BeOfType<CompletionStreamEvent.StreamError>()
            .Which.Message.Should().Contain("re-read what was just delivered");
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task SubscribeAsync_reopens_the_window_when_the_participant_repeats_the_checkpoint_it_opened_from()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, CheckpointWindow("5")),
            (HttpStatusCode.OK, TransactionWindow("6")));
        var client = ClientWith(transport);
        using var cancellation = new CancellationTokenSource();

        var observed = await DrainUntilAsync(
            client.SubscribeAsync<TestTemplate>(Alice, LedgerOffset.At(5), toOffset: null, cancellation.Token),
            cancellation,
            stopAfter: 2);

        observed[0].Should().BeOfType<ContractStreamEvent<TestTemplate>.Checkpoint>();
        observed[1].Should().BeOfType<ContractStreamEvent<TestTemplate>.Created>();
        transport.Requests.Should().HaveCount(2);
        transport.Requests[1].Body.Should().Contain("\"beginExclusive\":\"5\"");
    }

    [Fact]
    public async Task CompletionStreamAsync_reopens_the_window_when_the_participant_repeats_the_checkpoint_it_opened_from()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, CompletionCheckpointWindow("5")),
            (HttpStatusCode.OK, CompletionCheckpointWindow("6")));
        var client = ClientWith(transport);
        using var cancellation = new CancellationTokenSource();

        var observed = await DrainUntilAsync(
            client.CompletionStreamAsync(Alice, 5L, cancellation.Token), cancellation, stopAfter: 2);

        observed.Should().AllBeOfType<CompletionStreamEvent.Checkpoint>();
        transport.Requests.Should().HaveCount(2);
        transport.Requests[1].Body.Should().Contain("\"beginExclusive\":\"5\"");
    }

    [Fact]
    public async Task SubscribeAsync_warns_once_a_hundred_consecutive_windows_neither_advance_nor_hold()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, CheckpointWindow("5"));
        using var logs = new CapturingLoggerFactory();
        var client = ClientWith(transport, logger: logs.CreateLogger<RestLedgerClient>());
        using var cancellation = new CancellationTokenSource();

        await DrainUntilAsync(
            client.SubscribeAsync<TestTemplate>(Alice, LedgerOffset.At(5), toOffset: null, cancellation.Token),
            cancellation,
            stopAfter: 100);

        var warning = WarningsFrom(logs).Should().ContainSingle().Subject;
        warning.Should().Contain("100 consecutive windows");
        warning.Should().Contain("offset 5");
        warning.Should().Contain("stream_idle_timeout_ms");
    }

    [Fact]
    public async Task SubscribeAsync_stays_silent_below_a_hundred_consecutive_non_advancing_windows()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, CheckpointWindow("5"));
        using var logs = new CapturingLoggerFactory();
        var client = ClientWith(transport, logger: logs.CreateLogger<RestLedgerClient>());
        using var cancellation = new CancellationTokenSource();

        await DrainUntilAsync(
            client.SubscribeAsync<TestTemplate>(Alice, LedgerOffset.At(5), toOffset: null, cancellation.Token),
            cancellation,
            stopAfter: 99);

        WarningsFrom(logs).Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeAsync_stays_silent_while_the_participant_holds_each_non_advancing_window()
    {
        var transport = new RecordingHttpHandler()
            .WithResponse(HttpStatusCode.OK, CheckpointWindow("5"))
            .WithResponseDelay(TimeSpan.FromMilliseconds(15));
        using var logs = new CapturingLoggerFactory();
        var client = ClientWith(
            transport,
            windowIdleTimeout: TimeSpan.FromMilliseconds(10),
            logger: logs.CreateLogger<RestLedgerClient>());
        using var cancellation = new CancellationTokenSource();

        await DrainUntilAsync(
            client.SubscribeAsync<TestTemplate>(Alice, LedgerOffset.At(5), toOffset: null, cancellation.Token),
            cancellation,
            stopAfter: 110);

        WarningsFrom(logs).Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeAsync_restarts_the_count_when_a_window_advances_the_offset()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            [
                .. Enumerable.Repeat((HttpStatusCode.OK, CheckpointWindow("5")), 99),
                (HttpStatusCode.OK, TransactionWindow("6")),
                (HttpStatusCode.OK, CheckpointWindow("6")),
            ]);
        using var logs = new CapturingLoggerFactory();
        var client = ClientWith(transport, logger: logs.CreateLogger<RestLedgerClient>());
        using var cancellation = new CancellationTokenSource();

        await DrainUntilAsync(
            client.SubscribeAsync<TestTemplate>(Alice, LedgerOffset.At(5), toOffset: null, cancellation.Token),
            cancellation,
            stopAfter: 150);

        WarningsFrom(logs).Should().BeEmpty();
    }

    [Fact]
    public async Task CompletionStreamAsync_repeats_the_warning_at_each_doubling_of_the_non_advancing_run()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, CompletionCheckpointWindow("5"));
        using var logs = new CapturingLoggerFactory();
        var client = ClientWith(transport, logger: logs.CreateLogger<RestLedgerClient>());
        using var cancellation = new CancellationTokenSource();

        await DrainUntilAsync(client.CompletionStreamAsync(Alice, 5L, cancellation.Token), cancellation, stopAfter: 250);

        var warnings = WarningsFrom(logs);
        warnings.Should().HaveCount(2);
        warnings[0].Should().Contain("100 consecutive windows");
        warnings[1].Should().Contain("200 consecutive windows");
    }

    private static List<string> WarningsFrom(CapturingLoggerFactory logs) =>
        [.. logs.Records.Where(record => record.Level == LogLevel.Warning).Select(record => record.Message)];

    private static async Task<List<TEvent>> DrainUntilAsync<TEvent>(
        IAsyncEnumerable<TEvent> stream, CancellationTokenSource cancellation, int stopAfter)
    {
        var observed = new List<TEvent>();
        var drain = async () =>
        {
            await foreach (var streamEvent in stream)
            {
                observed.Add(streamEvent);
                if (observed.Count == stopAfter)
                {
                    await cancellation.CancelAsync();
                }
            }
        };

        await drain.Should().ThrowAsync<OperationCanceledException>();
        return observed;
    }
}
