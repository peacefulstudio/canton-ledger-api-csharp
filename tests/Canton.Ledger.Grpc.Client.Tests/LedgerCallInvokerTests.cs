// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Resilience;
using Com.Daml.Ledger.Api.V2;
using AwesomeAssertions;
using Grpc.Core;
using NSubstitute;
using Xunit;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerCallInvokerTests
{
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    private static LedgerClientOptions Options(TimeSpan? timeout = null, RetryOptions? retry = null) =>
        new()
        {
            GrpcAddress = "https://participant.example:6001",
            Timeout = timeout,
            Retry = retry ?? new RetryOptions(),
        };

    [Fact]
    public async Task GetHeadersAsync_attaches_a_bearer_token_from_the_provider()
    {
        var invoker = new LedgerCallInvoker(Options(), _tokenProvider);

        var headers = await invoker.GetHeadersAsync(TestContext.Current.CancellationToken);

        headers.Should().NotBeNull();
        headers!.GetValue("authorization").Should().Be("Bearer test-token");
    }

    [Fact]
    public async Task GetHeadersAsync_returns_null_when_unauthenticated()
    {
        var invoker = new LedgerCallInvoker(Options(), ITokenProvider.None);

        var headers = await invoker.GetHeadersAsync(TestContext.Current.CancellationToken);

        headers.Should().BeNull();
    }

    [Fact]
    public async Task GetHeadersAsync_throws_when_the_provider_returns_an_empty_token()
    {
        var emptyProvider = Substitute.For<ITokenProvider>();
        emptyProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("   ");
        var invoker = new LedgerCallInvoker(Options(), emptyProvider);

        var act = () => invoker.GetHeadersAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*returned an empty token*");
    }

    [Fact]
    public void GetDeadline_returns_null_when_no_timeout_is_configured()
    {
        var invoker = new LedgerCallInvoker(Options(timeout: null), _tokenProvider);

        invoker.GetDeadline().Should().BeNull();
    }

    [Fact]
    public void GetDeadline_returns_a_future_deadline_when_a_timeout_is_configured()
    {
        var invoker = new LedgerCallInvoker(Options(timeout: TimeSpan.FromSeconds(30)), _tokenProvider);

        invoker.GetDeadline().Should().NotBeNull().And.Subject.As<DateTime?>()!.Value.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public void TagServerCall_tags_the_activity_with_grpc_semconv_from_the_parsed_endpoint()
    {
        var invoker = new LedgerCallInvoker(Options(), _tokenProvider);

        using var source = new ActivitySource("LedgerCallInvokerTests.TagServerCall");
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("call");
        activity.Should().NotBeNull();

        invoker.TagServerCall(activity, CommandService.Descriptor, "Submit");

        activity!.GetTagItem(ActivityHelper.RpcSystem).Should().Be("grpc");
        activity.GetTagItem(ActivityHelper.RpcService).Should().Be("com.daml.ledger.api.v2.CommandService");
        activity.GetTagItem(ActivityHelper.RpcMethod).Should().Be("Submit");
        activity.GetTagItem(ActivityHelper.ServerAddress).Should().Be("participant.example");
        activity.GetTagItem(ActivityHelper.ServerPort).Should().Be(6001);
    }

    [Fact]
    public async Task InvokeAsync_runs_the_call_once_and_returns_its_response_when_retry_is_disabled()
    {
        var invoker = new LedgerCallInvoker(Options(), _tokenProvider);
        var calls = 0;
        Metadata? seenHeaders = null;

        var response = await invoker.InvokeAsync(
            (headers, _, _) =>
            {
                calls++;
                seenHeaders = headers;
                return Ok(new GetLedgerEndResponse { Offset = 7L });
            },
            TestContext.Current.CancellationToken);

        calls.Should().Be(1);
        response.Offset.Should().Be(7L);
        seenHeaders.Should().NotBeNull("the invoker recomputes auth headers per attempt");
    }

    [Fact]
    public async Task InvokeAsync_does_not_retry_a_transient_failure_when_retry_is_disabled()
    {
        var invoker = new LedgerCallInvoker(Options(), _tokenProvider);
        var attempts = 0;

        var act = () => invoker.InvokeAsync(
            (_, _, _) =>
            {
                attempts++;
                return Faulted<GetLedgerEndResponse>(new RpcException(new Status(StatusCode.Unavailable, "down")));
            },
            TestContext.Current.CancellationToken).AsTask();

        await act.Should().ThrowAsync<RpcException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task InvokeTracedAsync_reclassifies_caller_cancellation_as_OperationCanceledException()
    {
        var invoker = new LedgerCallInvoker(Options(), _tokenProvider);
        using var source = new ActivitySource("LedgerCallInvokerTests.CancelReclassify");
        using var cts = new CancellationTokenSource();

        var act = () => invoker.InvokeTracedAsync<LedgerClient, GetLedgerEndResponse, long>(
            source,
            StateService.Descriptor,
            "GetLedgerEnd",
            (_, _, _) =>
            {
                cts.Cancel();
                return Faulted<GetLedgerEndResponse>(new RpcException(new Status(StatusCode.Cancelled, "cancelled")));
            },
            response => response.Offset,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task InvokeTracedAsync_rethrows_an_expected_failure_without_recording_it_on_the_span()
    {
        var invoker = new LedgerCallInvoker(Options(), _tokenProvider);
        using var source = new ActivitySource("LedgerCallInvokerTests.ExpectedFailure");
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var act = () => invoker.InvokeTracedAsync<LedgerClient, GetLedgerEndResponse, long>(
            source,
            StateService.Descriptor,
            "GetLedgerEnd",
            (_, _, _) => Faulted<GetLedgerEndResponse>(new RpcException(new Status(StatusCode.NotFound, "absent"))),
            response => response.Offset,
            TestContext.Current.CancellationToken,
            isExpectedFailure: ex => ex.StatusCode == StatusCode.NotFound);

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.NotFound);
        stopped.Should().ContainSingle().Which.Status.Should().Be(
            ActivityStatusCode.Unset,
            "an expected failure is left for the caller to translate and is not recorded as a span error");
    }

    private static AsyncUnaryCall<T> Ok<T>(T value) =>
        new(
            Task.FromResult(value),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    private static AsyncUnaryCall<T> Faulted<T>(RpcException exception) =>
        new(
            Task.FromException<T>(exception),
            Task.FromResult(new Metadata()),
            () => exception.Status,
            () => exception.Trailers ?? new Metadata(),
            () => { });
}
