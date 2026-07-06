// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Polly;
using Polly.Retry;

namespace Canton.Ledger.Kernel.Resilience;

/// <summary>
/// Builds the shared Polly <see cref="ResiliencePipeline"/> that the gRPC and (future)
/// HTTP/JSON clients may compose over their own transport-specific calls (ADR 0006). The
/// pipeline built here knows nothing about any transport — no <c>Grpc.Core</c> or
/// <c>System.Net.Http</c> exception types are referenced — keeping the kernel
/// transport-neutral.
/// </summary>
public static class RetryPipelineFactory
{
    /// <summary>
    /// Creates a <see cref="ResiliencePipeline"/> from <paramref name="options"/>.
    /// Returns <see cref="ResiliencePipeline.Empty"/> — a genuine no-op — when
    /// <see cref="RetryOptions.Enabled"/> is <see langword="false"/>, so a caller who has
    /// not opted in observes no behavioral change.
    /// </summary>
    /// <param name="options">The retry configuration.</param>
    /// <param name="shouldRetry">
    /// Transport-specific predicate deciding whether a failure is transient and worth retrying. The
    /// kernel stays transport-neutral (ADR 0006): a caller — e.g. the gRPC client — supplies this to
    /// classify its own exception types (<c>RpcException</c> / <c>StatusCode</c>), which never enter
    /// the kernel. When <see langword="null"/>, Polly's default (retry on any exception) applies.
    /// </param>
    /// <param name="onRetry">
    /// Optional observer invoked before each retry delay, e.g. to emit a telemetry span. Receives a
    /// transport-neutral <see cref="RetryAttempt"/>.
    /// </param>
    public static ResiliencePipeline Create(
        RetryOptions options,
        Func<Exception, bool>? shouldRetry = null,
        Action<RetryAttempt>? onRetry = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
            return ResiliencePipeline.Empty;

        var retryStrategy = new RetryStrategyOptions
        {
            MaxRetryAttempts = options.MaxRetryAttempts,
            Delay = options.Delay,
            BackoffType = DelayBackoffType.Exponential
        };

        if (shouldRetry is not null)
        {
            retryStrategy.ShouldHandle = args =>
                new ValueTask<bool>(args.Outcome.Exception is { } exception && shouldRetry(exception));
        }

        if (onRetry is not null)
        {
            retryStrategy.OnRetry = args =>
            {
                onRetry(new RetryAttempt(args.AttemptNumber, args.Outcome.Exception, args.RetryDelay));
                return default;
            };
        }

        return new ResiliencePipelineBuilder()
            .AddRetry(retryStrategy)
            .Build();
    }
}
