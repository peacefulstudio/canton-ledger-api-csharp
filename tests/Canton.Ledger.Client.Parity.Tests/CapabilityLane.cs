// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class CapabilityLane<TCapability>(TCapability capability, Func<ValueTask> disposeAsync)
    : IAsyncDisposable
{
    public TCapability Capability { get; } = capability;

    public ValueTask DisposeAsync() => disposeAsync();
}
