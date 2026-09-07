// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Telemetry;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using AwesomeAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using Completion = Com.Daml.Ledger.Api.V2.Completion;
using CompletionStreamEvent = Canton.Ledger.Abstractions.CompletionStreamEvent;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

[Collection("LedgerClient global ActivitySource")]
public sealed class LedgerClientAsyncSubmitTests : IDisposable
{
    private static readonly Party ActAs = new("party::alice");

    private readonly LedgerClientOptions _options;
    private readonly GrpcChannel _channel;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly UpdateService.UpdateServiceClient _updateService;
    private readonly StateService.StateServiceClient _stateService;
    private readonly CommandSubmissionService.CommandSubmissionServiceClient _submissionService;
    private readonly CommandCompletionService.CommandCompletionServiceClient _completionService;
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    public LedgerClientAsyncSubmitTests()
    {
        _options = new LedgerClientOptions
        {
            GrpcAddress = "https://localhost:5001",
            UserId = "test-user",
        };
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);

        var callInvoker = Substitute.For<CallInvoker>();
        _commandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(callInvoker);
        _updateService = Substitute.ForPartsOf<UpdateService.UpdateServiceClient>(callInvoker);
        _stateService = Substitute.ForPartsOf<StateService.StateServiceClient>(callInvoker);
        _submissionService = Substitute.ForPartsOf<CommandSubmissionService.CommandSubmissionServiceClient>(callInvoker);
        _completionService = Substitute.ForPartsOf<CommandCompletionService.CommandCompletionServiceClient>(callInvoker);
    }

    public void Dispose() => _channel.Dispose();

    private LedgerClient CreateClient() => new(
        _options,
        _channel,
        _commandService,
        _updateService,
        _stateService,
        _submissionService,
        _completionService,
        _tokenProvider);

    private static RuntimeCommands.CommandsSubmission Create(string commandId = "fire-cmd") =>
        RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(
                new RuntimeIdentifier("pkg", "Module", "Template"),
                new DamlRecord(null, [])))
            .WithActAs(ActAs)
            .WithCommandId(new RuntimeCommands.CommandId(commandId));

    [Fact]
    public async Task Submit_issues_command_through_submission_service_without_waiting()
    {
        SubmitRequest? captured = null;
        StubSubmit(r => captured = r);

        var client = CreateClient();
        _ = await client.SubmitAsync(Create(), cancellationToken: TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.Commands.Commands_.Should().ContainSingle();
        captured.Commands.CommandId.Should().Be("fire-cmd");
    }

    [Fact]
    public async Task Submit_returns_supplied_command_id_for_correlation()
    {
        StubSubmit();

        var client = CreateClient();
        var commandId = await client.SubmitAsync(Create("corr-123"), cancellationToken: TestContext.Current.CancellationToken);

        commandId.Value.Should().Be("corr-123");
    }

    [Fact]
    public async Task Submit_mints_command_id_when_omitted_sends_it_and_returns_it()
    {
        SubmitRequest? captured = null;
        StubSubmit(r => captured = r);

        var submission = RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(
                new RuntimeIdentifier("pkg", "Module", "Template"),
                new DamlRecord(null, [])))
            .WithActAs(ActAs);

        var client = CreateClient();
        var commandId = await client.SubmitAsync(submission, cancellationToken: TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        Guid.TryParse(captured!.Commands.CommandId, out _).Should().BeTrue(
            "an omitted command id is minted as a GUID");
        commandId.Value.Should().Be(captured.Commands.CommandId,
            "the minted id sent on the wire is the same id surfaced to the caller");
    }

    [Fact]
    public async Task SubmitAsync_throws_ArgumentNullException_for_null_submission()
    {
        var client = CreateClient();

        var act = async () => await client.SubmitAsync(null!, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("submission");
    }

    [Fact]
    public async Task CompletionStreamAsync_yields_completions()
    {
        StubCompletionStream(
            CompletionResponse(new Completion { CommandId = "c1", UpdateId = "u1" }),
            CompletionResponse(new Completion { CommandId = "c2", UpdateId = "u2" }));

        var client = CreateClient();
        var events = await CollectAsync(client.CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        var completions = events.Should().AllBeOfType<CompletionStreamEvent.CommandAccepted>().Subject.ToList();
        completions.Select(c => c.Completion.CommandId.Value).Should().Equal("c1", "c2");
        completions.Select(c => c.UpdateId).Should().Equal("u1", "u2");
    }

    [Fact]
    public async Task CompletionStreamAsync_yields_offset_checkpoints_alongside_completions()
    {
        StubCompletionStream(
            new CompletionStreamResponse { OffsetCheckpoint = new OffsetCheckpoint { Offset = 5L } },
            CompletionResponse(new Completion { CommandId = "c1" }));

        var client = CreateClient();
        var events = await CollectAsync(client.CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<CompletionStreamEvent.Checkpoint>()
            .Which.Offset.Should().Be(5L);
        events[1].Should().BeOfType<CompletionStreamEvent.CommandAccepted>()
            .Which.Completion.CommandId.Value.Should().Be("c1");
    }

    [Fact]
    public async Task CompletionStreamAsync_yields_resume_offset_from_checkpoint_when_no_completions_arrive()
    {
        StubCompletionStream(
            new CompletionStreamResponse { OffsetCheckpoint = new OffsetCheckpoint { Offset = 42L } });

        var client = CreateClient();
        var events = await CollectAsync(client.CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        events.Should().ContainSingle().Which.Should().BeOfType<CompletionStreamEvent.Checkpoint>()
            .Which.Offset.Should().Be(42L);
    }

    [Fact]
    public async Task CompletionStreamAsync_skips_response_with_no_variant_set_and_keeps_streaming()
    {
        StubCompletionStream(
            new CompletionStreamResponse(),
            CompletionResponse(new Completion { CommandId = "c1" }));

        var client = CreateClient();
        var events = await CollectAsync(client.CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        events.Should().ContainSingle().Which.Should().BeOfType<CompletionStreamEvent.CommandAccepted>()
            .Which.Completion.CommandId.Value.Should().Be("c1");
    }

    [Fact]
    public async Task CompletionStreamAsync_passes_parties_offset_and_user_id_to_request()
    {
        CompletionStreamRequest? captured = null;
        StubCompletionStream(r => captured = r);

        var submitter = new RuntimeCommands.SubmitterInfo(
            new HashSet<Party> { (Party)"alice", (Party)"bob" },
            new HashSet<Party> { (Party)"observer" });

        var client = CreateClient();
        _ = await CollectAsync(client.CompletionStreamAsync(submitter, beginExclusiveOffset: 99L, cancellationToken: TestContext.Current.CancellationToken));

        captured.Should().NotBeNull();
        captured!.BeginExclusive.Should().Be(99L);
        captured.UserId.Should().Be("test-user");
        captured.Parties.Should().BeEquivalentTo(["alice", "bob", "observer"]);
    }

    [Fact]
    public async Task CompletionStreamAsync_surfaces_RpcException_as_StreamError_event()
    {
        var rpcException = new RpcException(new Status(StatusCode.Unavailable, "transient down"));
        StubCompletionStreamFailure(rpcException);

        var client = CreateClient();
        var events = await CollectAsync(client.CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        var error = events.Should().ContainSingle().Subject
            .Should().BeOfType<CompletionStreamEvent.StreamError>().Subject;
        error.StatusCode.Should().Be((int)StatusCode.Unavailable);
        error.Message.Should().Contain("transient");
    }

    [Fact]
    public async Task CompletionStreamAsync_populates_StreamError_Category_and_SourceException_from_the_transport_fault()
    {
        var rpcException = CategorisedRpcException.WithCategory(
            StatusCode.Aborted, "PARTICIPANT_BACKPRESSURE", "the participant is overloaded", "2");
        StubCompletionStreamFailure(rpcException);

        var client = CreateClient();
        var events = await CollectAsync(client.CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        var error = events.Should().ContainSingle().Subject
            .Should().BeOfType<CompletionStreamEvent.StreamError>().Subject;
        error.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
        error.SourceException.Should().BeSameAs(rpcException);
    }

    [Fact]
    public async Task CompletionStreamAsync_populates_StreamError_ErrorId_from_the_transport_fault()
    {
        var rpcException = CategorisedRpcException.WithCategory(
            StatusCode.Aborted, "STALE_STREAM_AUTHORIZATION", "the user's rights changed", "2");
        StubCompletionStreamFailure(rpcException);

        var client = CreateClient();
        var events = await CollectAsync(client.CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        var error = events.Should().ContainSingle().Subject
            .Should().BeOfType<CompletionStreamEvent.StreamError>().Subject;
        error.ErrorId.Should().Be(
            "STALE_STREAM_AUTHORIZATION",
            "the category this fault shares with every other contention condition cannot say which one arrived");
        error.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
    }

    [Fact]
    public async Task CompletionStreamAsync_leaves_StreamError_ErrorId_null_when_the_fault_carries_no_structured_error()
    {
        StubCompletionStreamFailure(new RpcException(new Status(StatusCode.Unavailable, "transient down")));

        var client = CreateClient();
        var events = await CollectAsync(client.CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        events.Should().ContainSingle().Subject
            .Should().BeOfType<CompletionStreamEvent.StreamError>()
            .Subject.ErrorId.Should().BeNull(
                "a transport fault the participant attached no error to reports no code, and none is invented");
    }

    [Fact]
    public async Task CompletionStreamAsync_carries_the_decode_failure_as_SourceException_with_no_category()
    {
        StubCompletionStream(CompletionResponse(new Completion { Offset = 7L, UpdateId = "u1" }));

        var client = CreateClient();
        var events = await CollectAsync(client.CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        var error = events.Should().ContainSingle().Subject
            .Should().BeOfType<CompletionStreamEvent.StreamError>().Subject;
        error.SourceException.Should().NotBeNull(
            "the exception that could not decode the completion is the only diagnostic a caller has");
        error.Category.Should().BeNull("the call itself succeeded, so no participant category was reported");
    }

    [Fact]
    public async Task CompletionStreamAsync_yields_completions_already_read_before_surfacing_mid_stream_StreamError()
    {
        StubCompletionStreamFailureAfterItems(
            new RpcException(new Status(StatusCode.Unavailable, "stream aborted after two completions")),
            CompletionResponse(new Completion { CommandId = "c1" }),
            CompletionResponse(new Completion { CommandId = "c2" }));

        var client = CreateClient();
        var events = await CollectAsync(client.CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        events.Should().HaveCount(3, "both already-read completions are yielded before the terminal fault");
        events.OfType<CompletionStreamEvent.CommandAccepted>().Select(e => e.Completion.CommandId.Value)
            .Should().Equal("c1", "c2");
        events[2].Should().BeOfType<CompletionStreamEvent.StreamError>()
            .Which.StatusCode.Should().Be((int)StatusCode.Unavailable);
    }

    [Fact]
    public async Task CompletionStreamAsync_StreamError_message_falls_back_to_status_string_when_detail_empty()
    {
        // gRPC surfaces a server status with no message as Status.Detail == "" (empty,
        // not null), so a plain ?? never falls back — the StreamError diagnostic must
        // still be non-empty.
        StubCompletionStreamFailure(new RpcException(new Status(StatusCode.Unavailable, string.Empty)));

        var client = CreateClient();
        var events = await CollectAsync(client.CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        var error = events.Should().ContainSingle().Subject
            .Should().BeOfType<CompletionStreamEvent.StreamError>().Subject;
        error.StatusCode.Should().Be((int)StatusCode.Unavailable);
        error.Message.Should().NotBeNullOrEmpty(
            "an empty transport Detail must fall back to the status string, not silently empty the diagnostic Message");
    }

    [Fact]
    public async Task CompletionStreamAsync_surfaces_completion_with_no_command_id_as_terminal_StreamError()
    {
        StubCompletionStream(
            CompletionResponse(new Completion { Offset = 7L, UpdateId = "u1" }),
            CompletionResponse(new Completion { CommandId = "c2", UpdateId = "u2" }));

        var client = CreateClient();
        var events = await CollectAsync(client.CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        var error = events.Should().ContainSingle(
            "the undecodable completion terminates the stream, so the completion behind it is never yielded")
            .Subject.Should().BeOfType<CompletionStreamEvent.StreamError>().Subject;
        error.StatusCode.Should().Be(
            (int)StatusCode.OK,
            "the call itself succeeded — the message arrived and could not be read");
        error.Message.Should().NotBeNullOrEmpty().And.Contain("command_id");
    }

    [Fact]
    public async Task CompletionStreamAsync_yields_completions_read_before_an_undecodable_completion()
    {
        StubCompletionStream(
            CompletionResponse(new Completion { CommandId = "c1", UpdateId = "u1" }),
            CompletionResponse(new Completion { Offset = 7L, UpdateId = "u2" }),
            CompletionResponse(new Completion { CommandId = "c3", UpdateId = "u3" }));

        var client = CreateClient();
        var events = await CollectAsync(client.CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<CompletionStreamEvent.CommandAccepted>()
            .Which.Completion.CommandId.Value.Should().Be("c1");
        events[1].Should().BeOfType<CompletionStreamEvent.StreamError>()
            .Which.StatusCode.Should().Be((int)StatusCode.OK);
    }

    [Fact]
    public async Task CompletionStreamAsync_throws_OperationCanceledException_when_caller_cancels_mid_stream()
    {
        using var cts = new CancellationTokenSource();
        StubCompletionStream(CompletionResponse(new Completion { CommandId = "c1" }));

        var client = CreateClient();
        var act = async () =>
        {
            await foreach (var _ in client.CompletionStreamAsync(ActAs, cancellationToken: cts.Token))
            {
                cts.Cancel();
            }
        };

        var thrown = await act.Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.InnerException.Should().BeOfType<RpcException>()
            .Which.StatusCode.Should().Be(StatusCode.Cancelled);
        thrown.Which.CancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task SubmitAsync_surfaces_caller_cancellation_as_OperationCanceledException_carrying_the_RpcException()
    {
        using var cts = new CancellationTokenSource();
        var cancelled = new RpcException(new Status(StatusCode.Cancelled, "call cancelled"));
        StubSubmitFailure(cancelled, cts);

        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.GrpcLedgerClient);
        var client = CreateClient();

        var act = async () => await client.SubmitAsync(Create(), cancellationToken: cts.Token);

        var thrown = await act.Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.InnerException.Should().BeSameAs(cancelled);
        thrown.Which.CancellationToken.Should().Be(cts.Token);
        capture.Activities.Should().OnlyContain(activity => activity.Status != ActivityStatusCode.Error);
    }

    [Fact]
    public async Task SubmitAsync_records_a_server_side_cancellation_on_the_activity_and_rethrows()
    {
        var cancelled = new RpcException(new Status(StatusCode.Cancelled, "server cancelled"));
        StubSubmitFailure(cancelled, cancelOnCall: null);

        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.GrpcLedgerClient);
        var client = CreateClient();

        var act = async () => await client.SubmitAsync(Create(), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<RpcException>()).Which.Should().BeSameAs(cancelled);
        capture.Activities.Should().Contain(activity => activity.Status == ActivityStatusCode.Error);
    }

    private static AsyncUnaryCall<TResponse> FailedUnaryCall<TResponse>(RpcException exception) =>
        new(
            Task.FromException<TResponse>(exception),
            Task.FromResult(new Metadata()),
            () => exception.Status,
            () => exception.Trailers ?? new Metadata(),
            () => { });

    private void StubSubmitFailure(RpcException exception, CancellationTokenSource? cancelOnCall)
    {
        _submissionService
            .SubmitAsync(
                Arg.Any<SubmitRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cancelOnCall?.Cancel();
                return FailedUnaryCall<SubmitResponse>(exception);
            });
    }

    private static CompletionStreamResponse CompletionResponse(Completion completion) =>
        new() { Completion = completion };

    private void StubSubmit(Action<SubmitRequest>? capture = null)
    {
        _submissionService
            .SubmitAsync(
                Arg.Do<SubmitRequest>(r => capture?.Invoke(r)),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<SubmitResponse>(
                Task.FromResult(new SubmitResponse()),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
    }

    private void StubCompletionStream(params CompletionStreamResponse[] responses)
        => StubCompletionStream(capture: null, responses);

    private void StubCompletionStream(
        Action<CompletionStreamRequest>? capture,
        params CompletionStreamResponse[] responses)
    {
        var reader = new FakeStreamReader<CompletionStreamResponse>(responses);
        var call = MakeServerStreamingCall(reader);

        _completionService
            .CompletionStream(
                Arg.Do<CompletionStreamRequest>(r => capture?.Invoke(r)),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(call);
    }

    private void StubCompletionStreamFailure(RpcException exception)
    {
        var reader = new FakeStreamReader<CompletionStreamResponse>(
            Array.Empty<CompletionStreamResponse>(), exception);
        var call = MakeServerStreamingCall(reader);

        _completionService
            .CompletionStream(
                Arg.Any<CompletionStreamRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(call);
    }

    private void StubCompletionStreamFailureAfterItems(
        RpcException afterItemsException, params CompletionStreamResponse[] responses)
    {
        var reader = new FakeStreamReader<CompletionStreamResponse>(responses, afterItemsException);
        var call = MakeServerStreamingCall(reader);

        _completionService
            .CompletionStream(
                Arg.Any<CompletionStreamRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(call);
    }

    private static AsyncServerStreamingCall<TResponse> MakeServerStreamingCall<TResponse>(
        IAsyncStreamReader<TResponse> reader) =>
        new(
            reader,
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    private static async Task<List<TItem>> CollectAsync<TItem>(IAsyncEnumerable<TItem> source)
    {
        var list = new List<TItem>();
        await foreach (var item in source)
        {
            list.Add(item);
        }
        return list;
    }
}
