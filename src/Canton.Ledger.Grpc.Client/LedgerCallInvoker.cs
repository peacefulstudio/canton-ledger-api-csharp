// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Resilience;
using Canton.Ledger.Kernel.Telemetry;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Polly;

namespace Canton.Ledger.Grpc.Client;

internal sealed class LedgerCallInvoker
{
    internal static readonly ActivitySource Source = LedgerActivitySource.Create<LedgerClient>();

    private const string RetryAttemptActivityName = "LedgerClient.RetryAttempt";
    private const string RetryAttemptNumberTag = "retry.attempt";
    private const string RetryDelayTag = "retry.delay_ms";

    private readonly ResiliencePipeline _retryPipeline;
    private readonly ITokenProvider? _tokenProvider;
    private readonly LedgerClientOptions _options;
    private readonly string _serverAddress;
    private readonly int _serverPort;

    internal LedgerCallInvoker(LedgerClientOptions options, ITokenProvider? tokenProvider)
    {
        _options = options;
        _tokenProvider = tokenProvider;
        _retryPipeline = RetryPipelineFactory.Create(_options.Retry, IsTransientRpcFailure, RecordRetryAttempt);
        (_serverAddress, _serverPort) = ActivityHelper.ParseServerEndpoint(_options.GrpcAddress);
    }

    /// <summary>
    /// Runs a single unary RPC through the retry pipeline, wrapping only the transport call so
    /// command construction (which fixes a stable <c>command_id</c>) stays above the retry boundary.
    /// Auth headers and the per-attempt deadline are recomputed on each attempt, so every retry is
    /// granted a fresh budget rather than sharing one budget across the whole sequence. A non-null
    /// <paramref name="timeout"/> is the caller's per-call deadline and takes precedence over the
    /// <see cref="LedgerClientOptions.Timeout"/> default; when both are null the call carries no
    /// deadline. With retry disabled (the default) the pipeline is empty and the call runs exactly
    /// once. The caller's <paramref name="cancellationToken"/> halts retries promptly.
    /// </summary>
    internal ValueTask<TResponse> InvokeAsync<TResponse>(
        Func<Metadata?, DateTime?, CancellationToken, AsyncUnaryCall<TResponse>> call,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null) =>
        _retryPipeline.ExecuteAsync(
            async token =>
            {
                var headers = await GetHeadersAsync(token).ConfigureAwait(false);
                return await call(headers, GetDeadline(timeout), token).ConfigureAwait(false);
            },
            cancellationToken);

    /// <summary>
    /// Runs a single unary RPC inside a client span and the retry pipeline, projecting the response.
    /// The span is named <c>{TClient}.{caller}</c> on <paramref name="activitySource"/> and tagged with
    /// gRPC semantic-convention attributes for <paramref name="service"/>/<paramref name="method"/>;
    /// <paramref name="configureActivity"/> runs before the call for request-derived tags. A caller
    /// cancellation surfaces as <see cref="OperationCanceledException"/>; an
    /// <paramref name="isExpectedFailure"/> match is rethrown unrecorded for the caller to translate;
    /// any other <see cref="RpcException"/> is recorded on the span and rethrown.
    /// </summary>
    internal Task<TProjected> InvokeTracedAsync<TClient, TResponse, TProjected>(
        ActivitySource activitySource,
        ServiceDescriptor service,
        string method,
        Func<Metadata?, DateTime?, CancellationToken, AsyncUnaryCall<TResponse>> call,
        Func<TResponse, TProjected> project,
        CancellationToken cancellationToken,
        Action<Activity?>? configureActivity = null,
        Predicate<RpcException>? isExpectedFailure = null,
        TimeSpan? timeout = null,
        [CallerMemberName] string callerMemberName = "") =>
        ExecuteTracedAsync<TClient, TProjected>(
            activitySource,
            service,
            method,
            async (_, token) => project(await InvokeAsync(call, token, timeout).ConfigureAwait(false)),
            cancellationToken,
            configureActivity,
            isExpectedFailure,
            callerMemberName);

    /// <summary>
    /// Runs a single unary RPC inside a client span and the retry pipeline, discarding the response.
    /// Span and error semantics match <see cref="InvokeTracedAsync{TClient,TResponse,TProjected}"/>.
    /// </summary>
    internal Task InvokeTracedAsync<TClient, TResponse>(
        ActivitySource activitySource,
        ServiceDescriptor service,
        string method,
        Func<Metadata?, DateTime?, CancellationToken, AsyncUnaryCall<TResponse>> call,
        CancellationToken cancellationToken,
        Action<Activity?>? configureActivity = null,
        TimeSpan? timeout = null,
        [CallerMemberName] string callerMemberName = "") =>
        ExecuteTracedAsync<TClient, TResponse>(
            activitySource,
            service,
            method,
            (_, token) => InvokeAsync(call, token, timeout).AsTask(),
            cancellationToken,
            configureActivity,
            callerMemberName: callerMemberName);

    /// <summary>
    /// Opens a client span for <paramref name="service"/>/<paramref name="method"/> on
    /// <paramref name="activitySource"/> and runs <paramref name="body"/> inside it, so a multi-call
    /// operation (e.g. server-paginated reads) shares one span and one error envelope. Reclassifies a
    /// caller cancellation to <see cref="OperationCanceledException"/>, rethrows an
    /// <paramref name="isExpectedFailure"/> match unrecorded, and records any other
    /// <see cref="RpcException"/> on the span before rethrowing.
    /// </summary>
    internal async Task<T> ExecuteTracedAsync<TClient, T>(
        ActivitySource activitySource,
        ServiceDescriptor service,
        string method,
        Func<Activity?, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken,
        Action<Activity?>? configureActivity = null,
        Predicate<RpcException>? isExpectedFailure = null,
        [CallerMemberName] string callerMemberName = "")
    {
        using var activity = LedgerActivitySource.StartActivity<TClient>(activitySource, callerMemberName: callerMemberName);
        TagServerCall(activity, service, method);
        configureActivity?.Invoke(activity);

        try
        {
            return await body(activity, cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException ex) when (CallerCancellation.Signals(ex, cancellationToken))
        {
            throw CallerCancellation.AsOperationCanceled(ex, cancellationToken);
        }
        catch (RpcException ex) when (isExpectedFailure?.Invoke(ex) == true)
        {
            throw;
        }
        catch (RpcException ex)
        {
            activity.RecordGrpcError(ex);
            throw;
        }
    }

    internal Task<Metadata?> GetHeadersAsync(CancellationToken cancellationToken) =>
        CallContextHelper.GetHeadersAsync(_tokenProvider, cancellationToken);

    internal DateTime? GetDeadline(TimeSpan? perCallTimeout = null) =>
        CallContextHelper.GetDeadline(perCallTimeout ?? _options.Timeout);

    internal void TagServerCall(Activity? activity, ServiceDescriptor service, string method) =>
        activity.SetGrpcCallTags(service, method, _serverAddress, _serverPort);

    private static bool IsTransientRpcFailure(Exception exception) =>
        exception is RpcException { StatusCode: StatusCode.Unavailable or StatusCode.DeadlineExceeded };

    private static void RecordRetryAttempt(RetryAttempt attempt)
    {
        using var activity = Source.StartActivity(RetryAttemptActivityName, ActivityKind.Internal);
        if (activity is null) return;

        activity.SetTag(RetryAttemptNumberTag, attempt.AttemptNumber);
        activity.SetTag(RetryDelayTag, attempt.RetryDelay.TotalMilliseconds);
        if (attempt.Exception is RpcException rpcException)
        {
            activity.SetStatus(ActivityStatusCode.Error, rpcException.Status.Detail);
            activity.SetTag(ActivityHelper.RpcGrpcStatusCode, (int)rpcException.StatusCode);
            activity.SetTag(ActivityHelper.ErrorType, rpcException.StatusCode.ToString());
        }
    }
}
