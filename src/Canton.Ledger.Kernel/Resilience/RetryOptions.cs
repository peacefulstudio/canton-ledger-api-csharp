// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Kernel.Resilience;

/// <summary>
/// Configuration for the kernel's opt-in retry pipeline (ADR 0006). <see cref="Enabled"/>
/// defaults to <see langword="false"/>, so a transport that does not explicitly opt in
/// sees no retry behavior — <see cref="RetryPipelineFactory.Create"/> hands it a genuine
/// no-op pipeline.
/// </summary>
public sealed record RetryOptions
{
    /// <summary>Whether the retry pipeline is active. Default: <see langword="false"/>.</summary>
    public bool Enabled { get; init; }

    /// <summary>Maximum number of retry attempts once <see cref="Enabled"/> is <see langword="true"/>. Default: 3.</summary>
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>Base delay between attempts once <see cref="Enabled"/> is <see langword="true"/>. Default: 200ms.</summary>
    public TimeSpan Delay { get; init; } = TimeSpan.FromMilliseconds(200);
}
