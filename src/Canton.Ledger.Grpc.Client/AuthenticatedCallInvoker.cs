// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Grpc.Core;

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// The escape-hatch <see cref="CallInvoker"/> behind <c>CreateCallInvoker()</c>: wraps the
/// channel's own invoker and reuses the SDK's <see cref="LedgerCallInvoker"/> plumbing, so raw
/// generated stubs get the same bearer-token resolution (including the
/// <c>ITokenProvider.None</c> unauthenticated skip), the same default per-attempt deadline on
/// unary calls, and the same opt-in transient-failure retry pipeline as the typed surface —
/// with a caller-supplied <c>authorization</c> header or deadline always winning. Streaming
/// calls attach auth headers but, mirroring the typed streaming paths, carry no default
/// deadline and are never retried.
/// </summary>
internal sealed class AuthenticatedCallInvoker : CallInvoker
{
    private const string StartFailedMessage = "Unable to read the call result before the call has started.";

    private readonly CallInvoker _inner;
    private readonly LedgerCallInvoker _context;

    internal AuthenticatedCallInvoker(CallInvoker inner, LedgerCallInvoker context)
    {
        _inner = inner;
        _context = context;
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        using var call = AsyncUnaryCall(method, host, options, request);
        return call.ResponseAsync.GetAwaiter().GetResult();
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        AsyncUnaryCall<TResponse>? lastAttempt = null;
        var responseTask = _context.InvokeAsync(
            (headers, deadline, token) =>
            {
                var attempt = _inner.AsyncUnaryCall(
                    method, host, WithCallContext(options, headers, deadline, token), request);
                lastAttempt = attempt;
                return attempt;
            },
            options.CancellationToken).AsTask();

        return new AsyncUnaryCall<TResponse>(
            responseTask,
            ResponseHeadersAsync(),
            () => Started(lastAttempt).GetStatus(),
            () => Started(lastAttempt).GetTrailers(),
            () => lastAttempt?.Dispose());

        async Task<Metadata> ResponseHeadersAsync()
        {
            await responseTask.ConfigureAwait(false);
            return await Started(lastAttempt).ResponseHeadersAsync.ConfigureAwait(false);
        }
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        var callTask = StartAsync();
        return new AsyncServerStreamingCall<TResponse>(
            new DeferredStreamReader<TResponse>(ReaderAsync()),
            ResponseHeadersAsync(),
            () => Started(callTask).GetStatus(),
            () => Started(callTask).GetTrailers(),
            () => DisposeWhenStarted(callTask));

        async Task<AsyncServerStreamingCall<TResponse>> StartAsync() =>
            _inner.AsyncServerStreamingCall(
                method, host, await WithAuthHeadersAsync(options).ConfigureAwait(false), request);

        async Task<IAsyncStreamReader<TResponse>> ReaderAsync() =>
            (await callTask.ConfigureAwait(false)).ResponseStream;

        async Task<Metadata> ResponseHeadersAsync() =>
            await (await callTask.ConfigureAwait(false)).ResponseHeadersAsync.ConfigureAwait(false);
    }

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options)
    {
        var callTask = StartAsync();
        return new AsyncClientStreamingCall<TRequest, TResponse>(
            new DeferredClientStreamWriter<TRequest>(WriterAsync()),
            ResponseAsync(),
            ResponseHeadersAsync(),
            () => Started(callTask).GetStatus(),
            () => Started(callTask).GetTrailers(),
            () => DisposeWhenStarted(callTask));

        async Task<AsyncClientStreamingCall<TRequest, TResponse>> StartAsync() =>
            _inner.AsyncClientStreamingCall(
                method, host, await WithAuthHeadersAsync(options).ConfigureAwait(false));

        async Task<IClientStreamWriter<TRequest>> WriterAsync() =>
            (await callTask.ConfigureAwait(false)).RequestStream;

        async Task<TResponse> ResponseAsync() =>
            await (await callTask.ConfigureAwait(false)).ResponseAsync.ConfigureAwait(false);

        async Task<Metadata> ResponseHeadersAsync() =>
            await (await callTask.ConfigureAwait(false)).ResponseHeadersAsync.ConfigureAwait(false);
    }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options)
    {
        var callTask = StartAsync();
        return new AsyncDuplexStreamingCall<TRequest, TResponse>(
            new DeferredClientStreamWriter<TRequest>(WriterAsync()),
            new DeferredStreamReader<TResponse>(ReaderAsync()),
            ResponseHeadersAsync(),
            () => Started(callTask).GetStatus(),
            () => Started(callTask).GetTrailers(),
            () => DisposeWhenStarted(callTask));

        async Task<AsyncDuplexStreamingCall<TRequest, TResponse>> StartAsync() =>
            _inner.AsyncDuplexStreamingCall(
                method, host, await WithAuthHeadersAsync(options).ConfigureAwait(false));

        async Task<IClientStreamWriter<TRequest>> WriterAsync() =>
            (await callTask.ConfigureAwait(false)).RequestStream;

        async Task<IAsyncStreamReader<TResponse>> ReaderAsync() =>
            (await callTask.ConfigureAwait(false)).ResponseStream;

        async Task<Metadata> ResponseHeadersAsync() =>
            await (await callTask.ConfigureAwait(false)).ResponseHeadersAsync.ConfigureAwait(false);
    }

    private async Task<CallOptions> WithAuthHeadersAsync(CallOptions options)
    {
        var authHeaders = await _context.GetHeadersAsync(options.CancellationToken).ConfigureAwait(false);
        return WithAuthHeaders(options, authHeaders);
    }

    private static CallOptions WithCallContext(
        CallOptions options, Metadata? authHeaders, DateTime? deadline, CancellationToken cancellationToken)
    {
        var contextualized = WithAuthHeaders(options, authHeaders).WithCancellationToken(cancellationToken);
        return options.Deadline is null && deadline is not null
            ? contextualized.WithDeadline(deadline.Value)
            : contextualized;
    }

    private static CallOptions WithAuthHeaders(CallOptions options, Metadata? authHeaders)
    {
        if (authHeaders is null)
            return options;
        if (options.Headers is not { Count: > 0 } callerHeaders)
            return options.WithHeaders(authHeaders);

        var merged = new Metadata();
        foreach (var entry in callerHeaders)
            merged.Add(entry);
        foreach (var entry in authHeaders)
        {
            if (!ContainsKey(callerHeaders, entry.Key))
                merged.Add(entry);
        }

        return options.WithHeaders(merged);
    }

    private static bool ContainsKey(Metadata headers, string key) =>
        headers.Any(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));

    private static T Started<T>(Task<T> callTask) where T : class =>
        callTask.IsCompletedSuccessfully
            ? callTask.Result
            : throw new InvalidOperationException(StartFailedMessage);

    private static T Started<T>(T? call) where T : class =>
        call ?? throw new InvalidOperationException(StartFailedMessage);

    private static void DisposeWhenStarted<T>(Task<T> callTask) where T : IDisposable =>
        _ = callTask.ContinueWith(
            static task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    try
                    {
                        task.Result.Dispose();
                    }
                    catch (Exception)
                    {
                    }
                }
                else
                {
                    _ = task.Exception;
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private sealed class DeferredStreamReader<TResponse>(Task<IAsyncStreamReader<TResponse>> readerTask)
        : IAsyncStreamReader<TResponse>
    {
        public TResponse Current => Started(readerTask).Current;

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            var reader = await readerTask.ConfigureAwait(false);
            return await reader.MoveNext(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class DeferredClientStreamWriter<TRequest>(Task<IClientStreamWriter<TRequest>> writerTask)
        : IClientStreamWriter<TRequest>
    {
        public WriteOptions? WriteOptions { get; set; }

        public async Task WriteAsync(TRequest message)
        {
            var writer = await ResolveWriterAsync().ConfigureAwait(false);
            await writer.WriteAsync(message).ConfigureAwait(false);
        }

        public async Task WriteAsync(TRequest message, CancellationToken cancellationToken)
        {
            var writer = await ResolveWriterAsync().ConfigureAwait(false);
            await writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        }

        public async Task CompleteAsync()
        {
            var writer = await writerTask.ConfigureAwait(false);
            await writer.CompleteAsync().ConfigureAwait(false);
        }

        private async Task<IClientStreamWriter<TRequest>> ResolveWriterAsync()
        {
            var writer = await writerTask.ConfigureAwait(false);
            if (WriteOptions is not null)
                writer.WriteOptions = WriteOptions;
            return writer;
        }
    }
}
