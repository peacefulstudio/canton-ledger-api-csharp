// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Resilience;
using Com.Daml.Ledger.Api.V2;
using AwesomeAssertions;
using Google.Protobuf;
using Grpc.Core;
using NSubstitute;
using Xunit;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public class AuthenticatedCallInvokerTests
{
    private const string AuthorizationKey = "authorization";

    private static readonly Method<GetLedgerEndRequest, GetLedgerEndResponse> UnaryMethod =
        Method(MethodType.Unary, "GetLedgerEnd", GetLedgerEndRequest.Parser, GetLedgerEndResponse.Parser);

    private static readonly Method<GetLedgerEndRequest, GetLedgerEndResponse> ClientStreamingMethod =
        Method(MethodType.ClientStreaming, "FakeClientStreaming", GetLedgerEndRequest.Parser, GetLedgerEndResponse.Parser);

    private static readonly Method<GetUpdatesRequest, GetUpdatesResponse> ServerStreamingMethod =
        Method(MethodType.ServerStreaming, "GetUpdates", GetUpdatesRequest.Parser, GetUpdatesResponse.Parser);

    private static readonly Method<GetUpdatesRequest, GetUpdatesResponse> DuplexStreamingMethod =
        Method(MethodType.DuplexStreaming, "FakeDuplexStreaming", GetUpdatesRequest.Parser, GetUpdatesResponse.Parser);

    private readonly CallInvoker _inner = Substitute.For<CallInvoker>();

    private static LedgerClientOptions Options(TimeSpan? timeout = null, RetryOptions? retry = null) =>
        new()
        {
            GrpcAddress = "https://participant.example:6001",
            Timeout = timeout,
            Retry = retry ?? new RetryOptions(),
        };

    private AuthenticatedCallInvoker CreateInvoker(ITokenProvider tokenProvider, LedgerClientOptions? options = null) =>
        new(_inner, new LedgerCallInvoker(options ?? Options(), tokenProvider));

    [Fact]
    public async Task AsyncUnaryCall_attaches_a_bearer_authorization_header_from_the_token_provider()
    {
        var captured = StubUnary(Ok(new GetLedgerEndResponse { Offset = 7L }));
        var invoker = CreateInvoker(new StaticTokenProvider("test-token"));

        using var call = invoker.AsyncUnaryCall(
            UnaryMethod, host: null, CallOptionsFor(), new GetLedgerEndRequest());
        var response = await call.ResponseAsync;

        response.Offset.Should().Be(7L);
        AuthorizationHeaderOf(captured.Single().Headers).Should().Be("Bearer test-token");
    }

    [Fact]
    public async Task AsyncUnaryCall_attaches_no_authorization_header_for_ITokenProvider_None()
    {
        var captured = StubUnary(Ok(new GetLedgerEndResponse { Offset = 7L }));
        var invoker = CreateInvoker(ITokenProvider.None);

        using var call = invoker.AsyncUnaryCall(
            UnaryMethod, host: null, CallOptionsFor(), new GetLedgerEndRequest());
        _ = await call.ResponseAsync;

        AuthorizationHeaderOf(captured.Single().Headers).Should().BeNull(
            "ITokenProvider.None signals unauthenticated access, so no Authorization header is sent");
    }

    [Fact]
    public async Task AsyncUnaryCall_preserves_caller_supplied_headers_when_adding_the_authorization_header()
    {
        var captured = StubUnary(Ok(new GetLedgerEndResponse()));
        var invoker = CreateInvoker(new StaticTokenProvider("test-token"));
        var callerHeaders = new Metadata { { "traceparent", "00-abc-def-01" } };

        using var call = invoker.AsyncUnaryCall(
            UnaryMethod, host: null, CallOptionsFor(headers: callerHeaders), new GetLedgerEndRequest());
        _ = await call.ResponseAsync;

        var headers = captured.Single().Headers;
        headers!.GetValue("traceparent").Should().Be("00-abc-def-01");
        AuthorizationHeaderOf(headers).Should().Be("Bearer test-token");
    }

    [Fact]
    public async Task AsyncUnaryCall_keeps_a_caller_supplied_authorization_header_over_the_provider_token()
    {
        var captured = StubUnary(Ok(new GetLedgerEndResponse()));
        var invoker = CreateInvoker(new StaticTokenProvider("provider-token"));
        var callerHeaders = new Metadata { { AuthorizationKey, "Bearer caller-token" } };

        using var call = invoker.AsyncUnaryCall(
            UnaryMethod, host: null, CallOptionsFor(headers: callerHeaders), new GetLedgerEndRequest());
        _ = await call.ResponseAsync;

        captured.Single().Headers!.Where(entry => entry.Key == AuthorizationKey)
            .Should().ContainSingle().Which.Value.Should().Be("Bearer caller-token");
    }

    [Fact]
    public async Task AsyncUnaryCall_applies_the_configured_Timeout_as_deadline_when_the_caller_sets_none()
    {
        var captured = StubUnary(Ok(new GetLedgerEndResponse()));
        var invoker = CreateInvoker(
            new StaticTokenProvider("test-token"), Options(timeout: TimeSpan.FromSeconds(30)));

        var before = DateTime.UtcNow;
        using var call = invoker.AsyncUnaryCall(
            UnaryMethod, host: null, CallOptionsFor(), new GetLedgerEndRequest());
        _ = await call.ResponseAsync;

        captured.Single().Deadline.Should().NotBeNull()
            .And.Subject.As<DateTime?>()!.Value.Should().BeOnOrAfter(before.AddSeconds(29))
            .And.BeOnOrBefore(DateTime.UtcNow.AddSeconds(31));
    }

    [Fact]
    public async Task AsyncUnaryCall_keeps_the_caller_deadline_when_one_is_set()
    {
        var captured = StubUnary(Ok(new GetLedgerEndResponse()));
        var invoker = CreateInvoker(
            new StaticTokenProvider("test-token"), Options(timeout: TimeSpan.FromSeconds(30)));
        var callerDeadline = DateTime.UtcNow.AddSeconds(5);

        using var call = invoker.AsyncUnaryCall(
            UnaryMethod, host: null, CallOptionsFor(deadline: callerDeadline), new GetLedgerEndRequest());
        _ = await call.ResponseAsync;

        captured.Single().Deadline.Should().Be(callerDeadline);
    }

    [Fact]
    public async Task AsyncUnaryCall_carries_no_deadline_when_neither_caller_nor_options_set_one()
    {
        var captured = StubUnary(Ok(new GetLedgerEndResponse()));
        var invoker = CreateInvoker(new StaticTokenProvider("test-token"), Options(timeout: null));

        using var call = invoker.AsyncUnaryCall(
            UnaryMethod, host: null, CallOptionsFor(), new GetLedgerEndRequest());
        _ = await call.ResponseAsync;

        captured.Single().Deadline.Should().BeNull();
    }

    [Fact]
    public async Task AsyncUnaryCall_retries_transient_failures_with_fresh_auth_headers_per_attempt()
    {
        var captured = StubUnary(
            Faulted<GetLedgerEndResponse>(new RpcException(new Status(StatusCode.Unavailable, "down"))),
            Ok(new GetLedgerEndResponse { Offset = 7L }));
        var tokenProvider = Substitute.For<ITokenProvider>();
        tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("token-1", "token-2");
        var invoker = CreateInvoker(
            tokenProvider,
            Options(retry: new RetryOptions
            {
                Enabled = true,
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(1),
            }));

        using var call = invoker.AsyncUnaryCall(
            UnaryMethod, host: null, CallOptionsFor(), new GetLedgerEndRequest());
        var response = await call.ResponseAsync;

        response.Offset.Should().Be(7L);
        captured.Select(options => AuthorizationHeaderOf(options.Headers))
            .Should().Equal("Bearer token-1", "Bearer token-2");
    }

    [Fact]
    public void BlockingUnaryCall_attaches_a_bearer_authorization_header_from_the_token_provider()
    {
        var captured = StubUnary(Ok(new GetLedgerEndResponse { Offset = 7L }));
        var invoker = CreateInvoker(new StaticTokenProvider("test-token"));

        var response = invoker.BlockingUnaryCall(
            UnaryMethod, host: null, CallOptionsFor(), new GetLedgerEndRequest());

        response.Offset.Should().Be(7L);
        AuthorizationHeaderOf(captured.Single().Headers).Should().Be("Bearer test-token");
    }

    [Fact]
    public async Task AsyncServerStreamingCall_attaches_the_authorization_header_without_a_default_deadline()
    {
        var captured = new List<CallOptions>();
        _inner
            .AsyncServerStreamingCall(
                Arg.Any<Method<GetUpdatesRequest, GetUpdatesResponse>>(),
                Arg.Any<string?>(),
                Arg.Do<CallOptions>(captured.Add),
                Arg.Any<GetUpdatesRequest>())
            .Returns(ServerStreaming(new GetUpdatesResponse()));
        var invoker = CreateInvoker(
            new StaticTokenProvider("test-token"), Options(timeout: TimeSpan.FromSeconds(30)));

        using var call = invoker.AsyncServerStreamingCall(
            ServerStreamingMethod, host: null, CallOptionsFor(), new GetUpdatesRequest());
        var moved = await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken);

        moved.Should().BeTrue();
        call.ResponseStream.Current.Should().NotBeNull();
        AuthorizationHeaderOf(captured.Single().Headers).Should().Be("Bearer test-token");
        captured.Single().Deadline.Should().BeNull(
            "a server stream may legitimately outlive any unary deadline budget");
    }

    [Fact]
    public async Task AsyncClientStreamingCall_attaches_the_authorization_header_and_forwards_the_request_stream()
    {
        var captured = new List<CallOptions>();
        var writer = Substitute.For<IClientStreamWriter<GetLedgerEndRequest>>();
        _inner
            .AsyncClientStreamingCall(
                Arg.Any<Method<GetLedgerEndRequest, GetLedgerEndResponse>>(),
                Arg.Any<string?>(),
                Arg.Do<CallOptions>(captured.Add))
            .Returns(ClientStreaming(writer, new GetLedgerEndResponse { Offset = 7L }));
        var invoker = CreateInvoker(new StaticTokenProvider("test-token"));

        using var call = invoker.AsyncClientStreamingCall(ClientStreamingMethod, host: null, CallOptionsFor());
        var request = new GetLedgerEndRequest();
        await call.RequestStream.WriteAsync(request, TestContext.Current.CancellationToken);
        await call.RequestStream.CompleteAsync();
        var response = await call.ResponseAsync;

        response.Offset.Should().Be(7L);
        await writer.Received(1).WriteAsync(request, Arg.Any<CancellationToken>());
        await writer.Received(1).CompleteAsync();
        AuthorizationHeaderOf(captured.Single().Headers).Should().Be("Bearer test-token");
    }

    [Fact]
    public async Task AsyncDuplexStreamingCall_attaches_the_authorization_header_and_bridges_both_streams()
    {
        var captured = new List<CallOptions>();
        var writer = Substitute.For<IClientStreamWriter<GetUpdatesRequest>>();
        _inner
            .AsyncDuplexStreamingCall(
                Arg.Any<Method<GetUpdatesRequest, GetUpdatesResponse>>(),
                Arg.Any<string?>(),
                Arg.Do<CallOptions>(captured.Add))
            .Returns(DuplexStreaming(writer, new GetUpdatesResponse()));
        var invoker = CreateInvoker(new StaticTokenProvider("test-token"));

        using var call = invoker.AsyncDuplexStreamingCall(DuplexStreamingMethod, host: null, CallOptionsFor());
        var request = new GetUpdatesRequest();
        await call.RequestStream.WriteAsync(request, TestContext.Current.CancellationToken);
        var moved = await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken);

        moved.Should().BeTrue();
        await writer.Received(1).WriteAsync(request, Arg.Any<CancellationToken>());
        AuthorizationHeaderOf(captured.Single().Headers).Should().Be("Bearer test-token");
    }

    private static CallOptions CallOptionsFor(Metadata? headers = null, DateTime? deadline = null) =>
        new(headers: headers, deadline: deadline, cancellationToken: TestContext.Current.CancellationToken);

    private static string? AuthorizationHeaderOf(Metadata? headers) =>
        headers?.FirstOrDefault(entry => entry.Key == AuthorizationKey)?.Value;

    private List<CallOptions> StubUnary(params AsyncUnaryCall<GetLedgerEndResponse>[] calls)
    {
        var captured = new List<CallOptions>();
        _inner
            .AsyncUnaryCall(
                Arg.Any<Method<GetLedgerEndRequest, GetLedgerEndResponse>>(),
                Arg.Any<string?>(),
                Arg.Do<CallOptions>(captured.Add),
                Arg.Any<GetLedgerEndRequest>())
            .Returns(calls[0], calls[1..]);
        return captured;
    }

    private static Method<TRequest, TResponse> Method<TRequest, TResponse>(
        MethodType type, string name, MessageParser<TRequest> requestParser, MessageParser<TResponse> responseParser)
        where TRequest : IMessage<TRequest>
        where TResponse : IMessage<TResponse> =>
        new(
            type,
            "com.daml.ledger.api.v2.FakeService",
            name,
            Marshallers.Create(message => message.ToByteArray(), requestParser.ParseFrom),
            Marshallers.Create(message => message.ToByteArray(), responseParser.ParseFrom));

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
            () => exception.Trailers,
            () => { });

    private static AsyncServerStreamingCall<T> ServerStreaming<T>(params T[] items) =>
        new(
            new FakeStreamReader<T>(items),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    private static AsyncClientStreamingCall<TRequest, TResponse> ClientStreaming<TRequest, TResponse>(
        IClientStreamWriter<TRequest> writer, TResponse response) =>
        new(
            writer,
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    private static AsyncDuplexStreamingCall<TRequest, TResponse> DuplexStreaming<TRequest, TResponse>(
        IClientStreamWriter<TRequest> writer, params TResponse[] items) =>
        new(
            writer,
            new FakeStreamReader<TResponse>(items),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
}
