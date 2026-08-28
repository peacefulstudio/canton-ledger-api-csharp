// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Kernel.Resilience;

/// <summary>
/// A transport-neutral description of a single retry attempt, handed to the
/// <c>onRetry</c> observer supplied to <see cref="RetryPipelineFactory.Create"/>. Carries only
/// BCL types so the kernel stays free of any transport (gRPC / HTTP) knowledge; a
/// consumer that wants transport-specific detail inspects <see cref="Exception"/> in its own layer.
/// </summary>
/// <param name="AttemptNumber">The zero-based number of the attempt that just failed and is being retried.</param>
/// <param name="Exception">The failure that triggered the retry, or <see langword="null"/> for a non-exception outcome.</param>
/// <param name="RetryDelay">The delay the pipeline will wait before the next attempt.</param>
public readonly record struct RetryAttempt(int AttemptNumber, Exception? Exception, TimeSpan RetryDelay);
