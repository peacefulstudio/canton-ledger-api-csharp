// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Telemetry;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using AwesomeAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using CompletionStreamEvent = Canton.Ledger.Abstractions.CompletionStreamEvent;

namespace Canton.Ledger.Grpc.Client.Tests;

[Collection("LedgerClient global ActivitySource")]
public sealed class LedgerClientStreamFaultTests : IDisposable
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

    public LedgerClientStreamFaultTests()
    {
        _options = new LedgerClientOptions { GrpcAddress = "https://localhost:5001", UserId = "test-user" };
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);

        var callInvoker = Substitute.For<CallInvoker>();
        _commandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(callInvoker);
        _updateService = Substitute.ForPartsOf<UpdateService.UpdateServiceClient>(callInvoker);
        _stateService = Substitute.ForPartsOf<StateService.StateServiceClient>(callInvoker);
        _submissionService = Substitute.ForPartsOf<CommandSubmissionService.CommandSubmissionServiceClient>(callInvoker);
        _completionService = Substitute.ForPartsOf<CommandCompletionService.CommandCompletionServiceClient>(callInvoker);
    }

    public void Dispose() => _channel.Dispose();

    private LedgerClient CreateClient(ILogger<LedgerClient>? logger = null) => new(
        _options,
        _channel,
        _commandService,
        _updateService,
        _stateService,
        _submissionService,
        _completionService,
        _tokenProvider,
        versionService: null,
        interactiveSubmissionService: null,
        logger: logger);

    [Fact]
    public async Task CompletionStreamAsync_logs_the_fault_status_by_name()
    {
        var detail = $"completion stream down {Guid.NewGuid()}";
        StubCompletionStreamFailure(new RpcException(new Status(StatusCode.Unavailable, detail)));
        using var logs = new CapturingLoggerFactory();

        await CollectAsync(CreateClient(logs.CreateLogger<LedgerClient>())
            .CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        StreamFailureLog(logs).Should().Be($"Completion stream failed: {StatusCode.Unavailable} {detail}");
    }

    [Fact]
    public async Task SubscribeAsync_logs_the_fault_status_by_name()
    {
        var detail = $"update stream down {Guid.NewGuid()}";
        StubGetUpdatesFailure(new RpcException(new Status(StatusCode.Unavailable, detail)));
        using var logs = new CapturingLoggerFactory();

        await CollectAsync(CreateClient(logs.CreateLogger<LedgerClient>())
            .SubscribeAsync<FooBar>(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        StreamFailureLog(logs).Should().Be(
            $"Subscribe stream failed for {nameof(FooBar)}: {StatusCode.Unavailable} {detail}");
    }

    [Fact]
    public async Task CompletionStreamAsync_falls_back_to_the_exception_message_when_the_status_carries_no_detail()
    {
        var fault = new RpcException(new Status(StatusCode.Unavailable, string.Empty));
        StubCompletionStreamFailure(fault);
        using var logs = new CapturingLoggerFactory();

        var events = await CollectAsync(CreateClient(logs.CreateLogger<LedgerClient>())
            .CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        events.Should().ContainSingle()
            .Which.Should().BeOfType<CompletionStreamEvent.StreamError>()
            .Which.Message.Should().Be(fault.Message);
        StreamFailureLog(logs).Should().Be(
            $"Completion stream failed: {StatusCode.Unavailable} {fault.Message}");
    }

    private static string StreamFailureLog(CapturingLoggerFactory logs) =>
        logs.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning).Subject.Message;

    [Fact]
    public async Task CompletionStreamAsync_records_a_mid_stream_fault_on_the_span()
    {
        var detail = $"completion stream down {Guid.NewGuid()}";
        StubCompletionStreamFailure(new RpcException(new Status(StatusCode.Unavailable, detail)));
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.GrpcLedgerClient);

        var events = await CollectAsync(
            CreateClient().CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        events.Should().ContainSingle().Which.Should().BeOfType<CompletionStreamEvent.StreamError>();
        AssertFaultRecorded(capture, detail);
    }

    [Fact]
    public async Task SubscribeAsync_records_a_mid_stream_fault_on_the_span()
    {
        var detail = $"update stream down {Guid.NewGuid()}";
        StubGetUpdatesFailure(new RpcException(new Status(StatusCode.Unavailable, detail)));
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.GrpcLedgerClient);

        var events = await CollectAsync(
            CreateClient().SubscribeAsync<FooBar>(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        events.Should().ContainSingle().Which.Should().BeOfType<ContractStreamEvent<FooBar>.StreamError>();
        AssertFaultRecorded(capture, detail);
    }

    [Fact]
    public async Task SubscribeActiveAsync_records_a_mid_snapshot_fault_on_the_span()
    {
        var detail = $"snapshot stream down {Guid.NewGuid()}";
        StubGetActiveContractsFailure(new RpcException(new Status(StatusCode.Unavailable, detail)));
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.GrpcLedgerClient);

        var entries = await CollectAsync(CreateClient().SubscribeActiveAsync<FooBar>(
            ActAs, LedgerOffset.At(7L), TestContext.Current.CancellationToken));

        entries.Should().ContainSingle().Which.Should().BeOfType<AcsSnapshotEntry<FooBar>.StreamError>();
        AssertFaultRecorded(capture, detail);
    }

    private static void AssertFaultRecorded(ActivityCapture capture, string detail)
    {
        var activity = capture.Activities.Should().ContainSingle(a => a.StatusDescription == detail).Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem(ActivityHelper.ErrorType).Should().Be(StatusCode.Unavailable.ToString());
        activity.GetTagItem(ActivityHelper.RpcGrpcStatusCode).Should().Be((int)StatusCode.Unavailable);
    }

    private void StubCompletionStreamFailure(RpcException exception) =>
        _completionService
            .CompletionStream(
                Arg.Any<CompletionStreamRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(MakeServerStreamingCall(
                new FakeStreamReader<CompletionStreamResponse>([], exception)));

    private void StubGetUpdatesFailure(RpcException exception) =>
        _updateService
            .GetUpdates(
                Arg.Any<GetUpdatesRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(MakeServerStreamingCall(new FakeStreamReader<GetUpdatesResponse>([], exception)));

    private void StubGetActiveContractsFailure(RpcException exception) =>
        _stateService
            .GetActiveContracts(
                Arg.Any<GetActiveContractsRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(MakeServerStreamingCall(
                new FakeStreamReader<GetActiveContractsResponse>([], exception)));

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
        var items = new List<TItem>();
        await foreach (var item in source)
        {
            items.Add(item);
        }
        return items;
    }
}
