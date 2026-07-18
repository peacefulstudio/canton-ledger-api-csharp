// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Authentication;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerClientAsyncSubmitTests
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
        _ = await client.SubmitAsync(Create(), TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.Commands.Commands_.Should().ContainSingle();
        captured.Commands.CommandId.Should().Be("fire-cmd");
    }

    [Fact]
    public async Task Submit_returns_supplied_command_id_for_correlation()
    {
        StubSubmit();

        var client = CreateClient();
        var commandId = await client.SubmitAsync(Create("corr-123"), TestContext.Current.CancellationToken);

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
        var commandId = await client.SubmitAsync(submission, TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        Guid.TryParse(captured!.Commands.CommandId, out _).Should().BeTrue(
            "an omitted command id is minted as a GUID");
        commandId.Value.Should().Be(captured.Commands.CommandId,
            "the minted id sent on the wire is the same id surfaced to the caller");
    }

    [Fact]
    public async Task CompletionStreamAsync_yields_completions()
    {
        StubCompletionStream(
            CompletionResponse(new Completion { CommandId = "c1", UpdateId = "u1" }),
            CompletionResponse(new Completion { CommandId = "c2", UpdateId = "u2" }));

        var client = CreateClient();
        var events = await CollectAsync(client.CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        var completions = events.Should().AllBeOfType<CompletionStreamEvent.CommandCompleted>().Subject.ToList();
        completions.Select(c => c.Completion.CommandId).Should().Equal("c1", "c2");
        completions.Select(c => c.Completion.UpdateId).Should().Equal("u1", "u2");
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
        events[1].Should().BeOfType<CompletionStreamEvent.CommandCompleted>()
            .Which.Completion.CommandId.Should().Be("c1");
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

        events.Should().ContainSingle().Which.Should().BeOfType<CompletionStreamEvent.CommandCompleted>()
            .Which.Completion.CommandId.Should().Be("c1");
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
    public async Task CompletionStreamAsync_yields_completions_already_read_before_surfacing_mid_stream_StreamError()
    {
        StubCompletionStreamFailureAfterItems(
            new RpcException(new Status(StatusCode.Unavailable, "stream aborted after two completions")),
            CompletionResponse(new Completion { CommandId = "c1" }),
            CompletionResponse(new Completion { CommandId = "c2" }));

        var client = CreateClient();
        var events = await CollectAsync(client.CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        events.Should().HaveCount(3, "both already-read completions are yielded before the terminal fault");
        events.OfType<CompletionStreamEvent.CommandCompleted>().Select(e => e.Completion.CommandId)
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
