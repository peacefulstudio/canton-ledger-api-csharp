// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestLedgerClientCompletionStreamTests : IDisposable
{
    private const string CompletionsPath = "/v2/commands/completions";

    private static readonly Party Alice = new("party::alice");
    private static readonly Party Bob = new("party::bob");
    private static readonly RuntimeCommands.SubmitterInfo AliceSubmitter =
        new(new HashSet<Party> { Alice }, new HashSet<Party> { Bob });

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

    private RestLedgerClient ClientWith(
        RecordingHttpHandler transport,
        string? userId = null,
        long? limit = null,
        TimeSpan? idleTimeout = null)
    {
        var options = new RestLedgerClientOptions
        {
            HttpAddress = "http://localhost:7575",
            UserId = userId,
        };

        if (limit is { } configuredLimit)
        {
            options.StreamWindowLimit = configuredLimit;
        }

        if (idleTimeout is { } configuredIdleTimeout)
        {
            options.StreamWindowIdleTimeout = configuredIdleTimeout;
        }

        return new RestLedgerClient(TrackedFactory(transport), Options.Create(options));
    }

    private static RecordingHttpHandler RespondingWith(string body) =>
        new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, body);

    private static async Task<List<CompletionStreamEvent>> DrainAsync(
        IAsyncEnumerable<CompletionStreamEvent> stream)
    {
        var events = new List<CompletionStreamEvent>();
        await foreach (var completionEvent in stream.WithCancellation(TestContext.Current.CancellationToken))
        {
            events.Add(completionEvent);
        }

        return events;
    }

    private static async Task<List<CompletionStreamEvent>> DrainUntilAsync(
        IAsyncEnumerable<CompletionStreamEvent> stream, CancellationTokenSource cancellation, int stopAfter)
    {
        var events = new List<CompletionStreamEvent>();
        var drain = async () =>
        {
            await foreach (var completionEvent in stream)
            {
                events.Add(completionEvent);
                if (events.Count == stopAfter)
                {
                    await cancellation.CancelAsync();
                }
            }
        };

        await drain.Should().ThrowAsync<OperationCanceledException>();
        return events;
    }

    private const string CheckpointWindow =
        """[{"completionResponse": {"OffsetCheckpoint": {"value": {"offset": "7"}}}}]""";

    [Fact]
    public async Task CompletionStreamAsync_posts_the_submitter_parties_and_begin_exclusive_offset_to_v2_commands_completions()
    {
        var transport = RespondingWith(CheckpointWindow);
        var client = ClientWith(transport, userId: "app-1");
        using var cancellation = new CancellationTokenSource();

        await DrainUntilAsync(
            client.CompletionStreamAsync(AliceSubmitter, 17L, cancellation.Token), cancellation, stopAfter: 1);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.RequestUri!.AbsolutePath.Should().Be(CompletionsPath);

        var request = JsonDocument.Parse(transport.LastRequestBody!).RootElement;
        request.GetProperty("beginExclusive").GetString().Should().Be("17");
        request.GetProperty("userId").GetString().Should().Be("app-1");
        request.GetProperty("parties").EnumerateArray().Select(party => party.GetString())
            .Should().BeEquivalentTo(["party::alice", "party::bob"]);
    }

    [Fact]
    public async Task CompletionStreamAsync_omits_the_user_id_when_none_is_configured()
    {
        var transport = RespondingWith(CheckpointWindow);
        var client = ClientWith(transport);
        using var cancellation = new CancellationTokenSource();

        await DrainUntilAsync(
            client.CompletionStreamAsync(AliceSubmitter, 0L, cancellation.Token), cancellation, stopAfter: 1);

        JsonDocument.Parse(transport.LastRequestBody!).RootElement
            .TryGetProperty("userId", out _).Should().BeFalse();
    }

    [Fact]
    public async Task CompletionStreamAsync_yields_one_event_per_array_entry_splitting_accepted_from_rejected()
    {
        var transport = RespondingWith(
            """
            [
              {"completionResponse": {"Completion": {"value": {"commandId": "cmd-1", "updateId": "update-1", "offset": "42", "actAs": ["party::alice"], "status": {"code": 0}}}}},
              {"completionResponse": {"Completion": {"value": {"commandId": "cmd-2", "offset": "43", "actAs": ["party::alice"], "status": {"code": 3, "message": "INVALID_ARGUMENT"}}}}}
            ]
            """);
        var client = ClientWith(transport);
        using var cancellation = new CancellationTokenSource();

        var events = await DrainUntilAsync(
            client.CompletionStreamAsync(AliceSubmitter, 0L, cancellation.Token), cancellation, stopAfter: 2);

        events.Should().HaveCount(2);
        var accepted = events[0].Should().BeOfType<CompletionStreamEvent.CommandAccepted>().Subject;
        accepted.UpdateId.Should().Be("update-1");
        accepted.Completion.CommandId.Value.Should().Be("cmd-1");
        accepted.Completion.Offset.Should().Be(42L);

        var rejected = events[1].Should().BeOfType<CompletionStreamEvent.CommandRejected>().Subject;
        rejected.Completion.CommandId.Value.Should().Be("cmd-2");
        rejected.Completion.Offset.Should().Be(43L);
        rejected.Status.Code.Should().Be(3);
        rejected.Status.Message.Should().Be("INVALID_ARGUMENT");
    }

    [Fact]
    public async Task CompletionStreamAsync_yields_a_checkpoint_for_an_offset_checkpoint_entry()
    {
        var transport = RespondingWith(
            """
            [
              {"completionResponse": {"OffsetCheckpoint": {"value": {"offset": "99"}}}},
              {"completionResponse": {"Completion": {"value": {"commandId": "cmd-1", "updateId": "update-1", "offset": "100", "actAs": ["party::alice"], "status": {"code": 0}}}}}
            ]
            """);
        var client = ClientWith(transport);
        using var cancellation = new CancellationTokenSource();

        var events = await DrainUntilAsync(
            client.CompletionStreamAsync(AliceSubmitter, 0L, cancellation.Token), cancellation, stopAfter: 2);

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<CompletionStreamEvent.Checkpoint>().Subject.Offset.Should().Be(99L);
        events[1].Should().BeOfType<CompletionStreamEvent.CommandAccepted>();
    }

    [Fact]
    public async Task CompletionStreamAsync_yields_nothing_for_an_empty_window_and_reopens_the_next_one_at_the_same_offset()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, "[]"),
            (HttpStatusCode.OK, CheckpointWindow));
        var client = ClientWith(transport);
        using var cancellation = new CancellationTokenSource();

        var events = await DrainUntilAsync(
            client.CompletionStreamAsync(AliceSubmitter, 3L, cancellation.Token), cancellation, stopAfter: 1);

        events.Should().ContainSingle().Which.Should().BeOfType<CompletionStreamEvent.Checkpoint>();
        transport.Requests.Should().HaveCount(2);
        transport.Requests[1].Body.Should().Contain("\"beginExclusive\":\"3\"");
    }

    [Fact]
    public async Task CompletionStreamAsync_skips_an_entry_carrying_neither_a_completion_nor_a_checkpoint()
    {
        var transport = RespondingWith(
            """
            [
              {"completionResponse": {"Empty": {"value": {}}}},
              {"completionResponse": {"OffsetCheckpoint": {"value": {"offset": "7"}}}}
            ]
            """);
        var client = ClientWith(transport);
        using var cancellation = new CancellationTokenSource();

        var events = await DrainUntilAsync(
            client.CompletionStreamAsync(AliceSubmitter, 0L, cancellation.Token), cancellation, stopAfter: 1);

        events.Should().ContainSingle()
            .Which.Should().BeOfType<CompletionStreamEvent.Checkpoint>()
            .Subject.Offset.Should().Be(7L);
    }

    [Fact]
    public async Task CompletionStreamAsync_ends_with_a_terminal_StreamError_on_a_non_success_response()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.ServiceUnavailable,
            """{"code": "PARTICIPANT_BACKPRESSURE", "cause": "the participant is overloaded", "errorCategory": 2}""");
        var client = ClientWith(transport);

        var events = await DrainAsync(client.CompletionStreamAsync(AliceSubmitter, 0L, TestContext.Current.CancellationToken));

        var error = events.Should().ContainSingle()
            .Which.Should().BeOfType<CompletionStreamEvent.StreamError>().Subject;
        error.StatusCode.Should().Be(503);
        error.Message.Should().Contain("the participant is overloaded");
    }

    [Fact]
    public async Task CompletionStreamAsync_populates_StreamError_Category_from_the_parsed_participant_error()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.ServiceUnavailable,
            """{"code": "PARTICIPANT_BACKPRESSURE", "cause": "the participant is overloaded", "errorCategory": 2}""");
        var client = ClientWith(transport);

        var events = await DrainAsync(client.CompletionStreamAsync(AliceSubmitter, 0L, TestContext.Current.CancellationToken));

        events.Should().ContainSingle()
            .Which.Should().BeOfType<CompletionStreamEvent.StreamError>()
            .Subject.Category.Should().Be(
                DamlErrorCategory.ContentionOnSharedResources,
                "a caller's retry policy switches on the parsed category, which is inert while the slot stays null");
    }

    [Fact]
    public async Task CompletionStreamAsync_leaves_StreamError_Category_null_when_the_body_carries_no_category()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.ServiceUnavailable, "");
        var client = ClientWith(transport);

        var events = await DrainAsync(client.CompletionStreamAsync(AliceSubmitter, 0L, TestContext.Current.CancellationToken));

        events.Should().ContainSingle()
            .Which.Should().BeOfType<CompletionStreamEvent.StreamError>()
            .Subject.Category.Should().BeNull(
                "an unclassifiable fault carries no category rather than the Unknown sentinel");
    }

    [Fact]
    public async Task CompletionStreamAsync_populates_StreamError_ErrorId_from_the_parsed_participant_error()
    {
        var fault = await TerminalFaultAsync(
            HttpStatusCode.Conflict,
            """{"code": "STALE_STREAM_AUTHORIZATION", "cause": "the user's rights changed", "errorCategory": 2}""");

        fault.ErrorId.Should().Be(
            "STALE_STREAM_AUTHORIZATION",
            "the participant's own error code is what tells a self-clearing fault from one that repeats identically");
    }

    [Fact]
    public async Task CompletionStreamAsync_leaves_StreamError_ErrorId_null_when_the_body_carries_no_structured_error()
    {
        var fault = await TerminalFaultAsync(HttpStatusCode.ServiceUnavailable, "");

        fault.ErrorId.Should().BeNull(
            "a failure the participant attached no structured error to has no code to hand on, and none is invented");
    }

    [Fact]
    public async Task CompletionStreamAsync_leaves_StreamError_ErrorId_null_when_the_success_body_will_not_decode()
    {
        var events = await DrainAsync(ClientWith(RespondingWith("""{"completionResponse": {}}"""))
            .CompletionStreamAsync(AliceSubmitter, 0L, TestContext.Current.CancellationToken));

        events.Should().ContainSingle()
            .Which.Should().BeOfType<CompletionStreamEvent.StreamError>()
            .Subject.ErrorId.Should().BeNull("the participant answered successfully, so it reported no error code");
    }

    [Fact]
    public async Task CompletionStreamAsync_tells_two_faults_of_one_status_and_category_apart_by_ErrorId()
    {
        var staleStream = await TerminalFaultAsync(
            HttpStatusCode.Conflict,
            """{"code": "STALE_STREAM_AUTHORIZATION", "cause": "the user's rights changed", "errorCategory": 2}""");
        var duplicateCommand = await TerminalFaultAsync(
            HttpStatusCode.Conflict,
            """{"code": "DUPLICATE_COMMAND", "cause": "already submitted", "errorCategory": 2}""");

        staleStream.StatusCode.Should().Be(duplicateCommand.StatusCode);
        staleStream.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources)
            .And.Be(duplicateCommand.Category);
        staleStream.ErrorId.Should().Be("STALE_STREAM_AUTHORIZATION");
        duplicateCommand.ErrorId.Should().Be(
            "DUPLICATE_COMMAND",
            "status and category are identical here, so only the error code separates a stream a reopen fixes from one it does not");
    }

    [Fact]
    public async Task CompletionStreamAsync_tells_the_stale_stream_fault_apart_from_the_window_limit_413_by_ErrorId()
    {
        var staleStream = await TerminalFaultAsync(
            HttpStatusCode.Conflict,
            """{"code": "STALE_STREAM_AUTHORIZATION", "cause": "the user's rights changed", "errorCategory": 2}""");
        var windowTooLarge = await TerminalFaultAsync(
            HttpStatusCode.RequestEntityTooLarge,
            """{"code": "RESULT_TOO_LARGE", "cause": "past http-list-max-elements-limit", "errorCategory": 8}""");

        staleStream.ErrorId.Should().Be("STALE_STREAM_AUTHORIZATION");
        windowTooLarge.ErrorId.Should().Be(
            "RESULT_TOO_LARGE",
            "reopening resolves the stale stream and reproduces the 413 with the same window limit, so a caller has to read which it holds");
    }

    private async Task<CompletionStreamEvent.StreamError> TerminalFaultAsync(HttpStatusCode status, string body)
    {
        var events = await DrainAsync(ClientWith(new RecordingHttpHandler().WithResponse(status, body))
            .CompletionStreamAsync(AliceSubmitter, 0L, TestContext.Current.CancellationToken));

        return events.Should().ContainSingle()
            .Which.Should().BeOfType<CompletionStreamEvent.StreamError>().Subject;
    }

    [Fact]
    public async Task CompletionStreamAsync_carries_the_decode_failure_as_SourceException_on_a_malformed_success_body()
    {
        var client = ClientWith(RespondingWith("""{"completionResponse": {}}"""));

        var events = await DrainAsync(client.CompletionStreamAsync(AliceSubmitter, 0L, TestContext.Current.CancellationToken));

        events.Should().ContainSingle()
            .Which.Should().BeOfType<CompletionStreamEvent.StreamError>()
            .Subject.SourceException.Should().NotBeNull(
                "the exception that could not read the body is the only diagnostic a caller has");
    }

    [Fact]
    public async Task CompletionStreamAsync_surfaces_a_413_as_a_StreamError_rather_than_throwing()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.RequestEntityTooLarge,
            """{"code": "RESULT_TOO_LARGE", "cause": "past http-list-max-elements-limit", "errorCategory": 8}""");
        var client = ClientWith(transport);

        var events = await DrainAsync(client.CompletionStreamAsync(AliceSubmitter, 0L, TestContext.Current.CancellationToken));

        events.Should().ContainSingle()
            .Which.Should().BeOfType<CompletionStreamEvent.StreamError>()
            .Subject.StatusCode.Should().Be(413);
    }

    [Fact]
    public async Task CompletionStreamAsync_ends_with_a_terminal_StreamError_when_a_completion_will_not_project()
    {
        var transport = RespondingWith(
            """
            [
              {"completionResponse": {"OffsetCheckpoint": {"value": {"offset": "5"}}}},
              {"completionResponse": {"Completion": {"value": {"commandId": "cmd-1", "updateId": "update-1", "offset": "not-an-offset", "actAs": ["party::alice"], "status": {"code": 0}}}}},
              {"completionResponse": {"OffsetCheckpoint": {"value": {"offset": "6"}}}}
            ]
            """);
        var client = ClientWith(transport);

        var events = await DrainAsync(client.CompletionStreamAsync(AliceSubmitter, 0L, TestContext.Current.CancellationToken));

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<CompletionStreamEvent.Checkpoint>().Subject.Offset.Should().Be(5L);
        var error = events[1].Should().BeOfType<CompletionStreamEvent.StreamError>().Subject;
        error.StatusCode.Should().Be(0);
        error.Message.Should().Contain("not-an-offset");
    }

    [Fact]
    public async Task CompletionStreamAsync_ends_with_a_terminal_StreamError_when_a_deduplication_duration_will_not_parse()
    {
        var transport = RespondingWith(
            """
            [
              {"completionResponse": {"Completion": {"value": {"commandId": "cmd-1", "updateId": "update-1", "offset": "42", "actAs": ["party::alice"], "status": {"code": 0}, "deduplicationPeriod": {"DeduplicationDuration": "not-a-duration"}}}}}
            ]
            """);
        var client = ClientWith(transport);

        var events = await DrainAsync(client.CompletionStreamAsync(AliceSubmitter, 0L, TestContext.Current.CancellationToken));

        events.Should().ContainSingle()
            .Which.Should().BeOfType<CompletionStreamEvent.StreamError>()
            .Subject.StatusCode.Should().Be(0);
    }

    [Fact]
    public async Task CompletionStreamAsync_ends_with_a_terminal_StreamError_on_a_malformed_success_body()
    {
        var client = ClientWith(RespondingWith("""{"completionResponse": {}}"""));

        var events = await DrainAsync(client.CompletionStreamAsync(AliceSubmitter, 0L, TestContext.Current.CancellationToken));

        events.Should().ContainSingle()
            .Which.Should().BeOfType<CompletionStreamEvent.StreamError>()
            .Subject.StatusCode.Should().Be(0);
    }

    [Fact]
    public async Task CompletionStreamAsync_ends_with_a_terminal_StreamError_when_the_success_body_carries_a_null_entry()
    {
        var client = ClientWith(RespondingWith("[null]"));

        var events = await DrainAsync(client.CompletionStreamAsync(AliceSubmitter, 0L, TestContext.Current.CancellationToken));

        events.Should().ContainSingle()
            .Which.Should().BeOfType<CompletionStreamEvent.StreamError>()
            .Subject.StatusCode.Should().Be(0);
    }

    [Fact]
    public async Task CompletionStreamAsync_throws_the_transport_failure_that_never_reached_the_participant()
    {
        var transport = new RecordingHttpHandler()
            .WithTransportException(new HttpRequestException("connection refused"));
        var client = ClientWith(transport);

        var act = () => DrainAsync(client.CompletionStreamAsync(AliceSubmitter, 0L, TestContext.Current.CancellationToken));

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task CompletionStreamAsync_sends_the_configured_window_bounds_as_query_parameters()
    {
        var transport = RespondingWith(CheckpointWindow);
        var client = ClientWith(transport, limit: 25L, idleTimeout: TimeSpan.FromSeconds(3));
        using var cancellation = new CancellationTokenSource();

        await DrainUntilAsync(
            client.CompletionStreamAsync(AliceSubmitter, 0L, cancellation.Token), cancellation, stopAfter: 1);

        transport.LastRequest!.RequestUri!.PathAndQuery
            .Should().Be($"{CompletionsPath}?limit=25&stream_idle_timeout_ms=3000");
    }

    [Fact]
    public async Task CompletionStreamAsync_sends_the_documented_defaults_when_neither_bound_is_configured()
    {
        var transport = RespondingWith(CheckpointWindow);
        var client = ClientWith(transport);
        using var cancellation = new CancellationTokenSource();

        await DrainUntilAsync(
            client.CompletionStreamAsync(AliceSubmitter, 0L, cancellation.Token), cancellation, stopAfter: 1);

        transport.LastRequest!.RequestUri!.PathAndQuery
            .Should().Be($"{CompletionsPath}?limit=200&stream_idle_timeout_ms=2000");
    }

    [Fact]
    public async Task CompletionStreamAsync_throws_OperationCanceledException_when_the_token_is_already_cancelled()
    {
        var client = ClientWith(RespondingWith("[]"));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var act = async () =>
        {
            await foreach (var _ in client.CompletionStreamAsync(AliceSubmitter, 0L, cancellation.Token))
            {
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CompletionStreamAsync_stops_enumerating_when_the_token_is_cancelled_mid_window()
    {
        var transport = RespondingWith(
            """
            [
              {"completionResponse": {"OffsetCheckpoint": {"value": {"offset": "1"}}}},
              {"completionResponse": {"OffsetCheckpoint": {"value": {"offset": "2"}}}}
            ]
            """);
        var client = ClientWith(transport);
        using var cancellation = new CancellationTokenSource();

        var observed = new List<CompletionStreamEvent>();
        var act = async () =>
        {
            await foreach (var completionEvent in client.CompletionStreamAsync(AliceSubmitter, 0L, cancellation.Token))
            {
                observed.Add(completionEvent);
                await cancellation.CancelAsync();
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        observed.Should().ContainSingle();
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void StreamWindowLimit_fails_validation_when_it_is_not_positive(long limit)
    {
        var options = new RestLedgerClientOptions
        {
            HttpAddress = "http://localhost:7575",
            StreamWindowLimit = limit,
        };

        options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options))
            .Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain(nameof(RestLedgerClientOptions.StreamWindowLimit));
    }

    [Fact]
    public void StreamWindowIdleTimeout_fails_validation_when_it_is_not_positive()
    {
        var options = new RestLedgerClientOptions
        {
            HttpAddress = "http://localhost:7575",
            StreamWindowIdleTimeout = TimeSpan.Zero,
        };

        options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options))
            .Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain(nameof(RestLedgerClientOptions.StreamWindowIdleTimeout));
    }

    [Fact]
    public void StreamWindowLimit_and_StreamWindowIdleTimeout_default_to_the_values_Canton_documents()
    {
        var options = new RestLedgerClientOptions { HttpAddress = "http://localhost:7575" };

        options.StreamWindowLimit.Should().Be(200L);
        options.StreamWindowIdleTimeout.Should().Be(TimeSpan.FromSeconds(2));
        options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options))
            .Should().BeEmpty();
    }
}
