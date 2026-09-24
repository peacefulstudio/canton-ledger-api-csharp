// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Diagnostics;

namespace Canton.Ledger.Benchmarks;

internal sealed class ArrivalBoard
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<long>> _arrivals = new(StringComparer.Ordinal);

    public void Arrived(string key) => Slot(key).TrySetResult(Stopwatch.GetTimestamp());

    public Task<long> ArrivalOf(string key) => Slot(key).Task;

    private TaskCompletionSource<long> Slot(string key) =>
        _arrivals.GetOrAdd(key, _ => new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously));
}
