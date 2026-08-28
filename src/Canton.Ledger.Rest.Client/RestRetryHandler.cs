// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Canton.Ledger.Kernel.Resilience;
using Canton.Ledger.Kernel.Telemetry;
using Microsoft.Extensions.Options;
using Polly;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Applies the kernel's opt-in retry pipeline to every JSON Ledger API request, mirroring the gRPC
/// client's <c>LedgerCallInvoker</c>. Registered outermost in the handler chain, so each attempt
/// resolves a fresh bearer token and emits its own client span. Register the source as
/// <c>tracing.AddSource(RestLedgerClient.ActivitySourceName)</c> to see the retry spans.
/// </summary>
internal sealed class RestRetryHandler : DelegatingHandler
{
    internal const string RetryAttemptActivityName = "RestLedgerClient.RetryAttempt";
    internal const string RetryAttemptNumberTag = "retry.attempt";
    internal const string RetryDelayTag = "retry.delay_ms";

    private static readonly ActivitySource Source = LedgerActivitySource.Create<RestLedgerClient>();

    private readonly ResiliencePipeline _retryPipeline;
    private readonly bool _enabled;

    public RestRetryHandler(IOptions<RestLedgerClientOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var retry = options.Value.Retry;
        _enabled = retry.Enabled;
        _retryPipeline = RetryPipelineFactory.Create(retry, IsTransientHttpFailure, RecordRetryAttempt);
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!_enabled)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        await BufferBodySoEveryAttemptCanReplayItAsync(request, cancellationToken).ConfigureAwait(false);

        return await _retryPipeline
            .ExecuteAsync(async token => await base.SendAsync(request, token).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task BufferBodySoEveryAttemptCanReplayItAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Content is not null)
            await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsTransientHttpFailure(Exception exception) => exception switch
    {
        HttpRequestException => true,
        TimeoutException => true,
        TaskCanceledException { InnerException: TimeoutException } => true,
        _ => false
    };

    private static void RecordRetryAttempt(RetryAttempt attempt)
    {
        using var activity = Source.StartActivity(RetryAttemptActivityName, ActivityKind.Internal);
        if (activity is null) return;

        activity.SetTag(RetryAttemptNumberTag, attempt.AttemptNumber);
        activity.SetTag(RetryDelayTag, attempt.RetryDelay.TotalMilliseconds);
        if (attempt.Exception is { } exception)
        {
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity.SetTag(SemanticConventions.ErrorType, exception.GetType().FullName);
        }
    }
}
