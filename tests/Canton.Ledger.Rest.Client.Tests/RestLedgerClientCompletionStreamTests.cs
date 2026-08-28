// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Data;
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
        TimeSpan? idleTimeout = null) =>
        new(TrackedFactory(transport), Options.Create(new RestLedgerClientOptions
        {
            HttpAddress = "http://localhost:7575",
            UserId = userId,
            CompletionStreamLimit = limit,
            CompletionStreamIdleTimeout = idleTimeout,
        }));

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

    [Fact]
    public async Task CompletionStreamAsync_posts_the_submitter_parties_and_begin_exclusive_offset_to_v2_commands_completions()
    {
        var transport = RespondingWith("[]");
        var client = ClientWith(transport, userId: "app-1");

        await DrainAsync(client.CompletionStreamAsync(AliceSubmitter, 17L, TestContext.Current.CancellationToken));

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
        var transport = RespondingWith("[]");
        var client = ClientWith(transport);

        await DrainAsync(client.CompletionStreamAsync(AliceSubmitter, 0L, TestContext.Current.CancellationToken));

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

        var events = await DrainAsync(client.CompletionStreamAsync(AliceSubmitter, 0L, TestContext.Current.CancellationToken));

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

        var events = await DrainAsync(client.CompletionStreamAsync(AliceSubmitter, 0L, TestContext.Current.CancellationToken));

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<CompletionStreamEvent.Checkpoint>().Subject.Offset.Should().Be(99L);
        events[1].Should().BeOfType<CompletionStreamEvent.CommandAccepted>();
    }

    [Fact]
    public async Task CompletionStreamAsync_yields_nothing_for_an_empty_window()
    {
        var client = ClientWith(RespondingWith("[]"));

        var events = await DrainAsync(client.CompletionStreamAsync(AliceSubmitter, 0L, TestContext.Current.CancellationToken));

        events.Should().BeEmpty();
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

        var events = await DrainAsync(client.CompletionStreamAsync(AliceSubmitter, 0L, TestContext.Current.CancellationToken));

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
        var transport = RespondingWith("[]");
        var client = ClientWith(transport, limit: 25L, idleTimeout: TimeSpan.FromSeconds(3));

        await DrainAsync(client.CompletionStreamAsync(AliceSubmitter, 0L, TestContext.Current.CancellationToken));

        transport.LastRequest!.RequestUri!.PathAndQuery
            .Should().Be($"{CompletionsPath}?limit=25&stream_idle_timeout_ms=3000");
    }

    [Fact]
    public async Task CompletionStreamAsync_omits_the_window_query_parameters_when_neither_is_configured()
    {
        var transport = RespondingWith("[]");
        var client = ClientWith(transport);

        await DrainAsync(client.CompletionStreamAsync(AliceSubmitter, 0L, TestContext.Current.CancellationToken));

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be(CompletionsPath);
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
    public void CompletionStreamLimit_fails_validation_when_it_is_not_positive(long limit)
    {
        var options = new RestLedgerClientOptions
        {
            HttpAddress = "http://localhost:7575",
            CompletionStreamLimit = limit,
        };

        options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options))
            .Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain(nameof(RestLedgerClientOptions.CompletionStreamLimit));
    }

    [Fact]
    public void CompletionStreamIdleTimeout_fails_validation_when_it_is_not_positive()
    {
        var options = new RestLedgerClientOptions
        {
            HttpAddress = "http://localhost:7575",
            CompletionStreamIdleTimeout = TimeSpan.Zero,
        };

        options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options))
            .Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain(nameof(RestLedgerClientOptions.CompletionStreamIdleTimeout));
    }
}
