// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Daml.Runtime.Data;

namespace Canton.Ledger.Benchmarks;

internal static class SubmitScenarios
{
    public static async Task<LatencySummary> SequentialLatencyAsync(
        Transport transport, Party owner, int samples, CancellationToken cancellationToken)
    {
        var latencies = new List<TimeSpan>(samples);
        for (var i = 0; i < samples; i++)
        {
            var start = Stopwatch.GetTimestamp();
            await Seed.CreateMarkerAsync(transport.Client, owner, cancellationToken).ConfigureAwait(false);
            latencies.Add(Stopwatch.GetElapsedTime(start));
        }

        return LatencySummary.Of(latencies);
    }

    public static async Task<ThroughputSummary> ConcurrentThroughputAsync(
        Transport transport, Party owner, int commands, int concurrency, CancellationToken cancellationToken)
    {
        var start = Stopwatch.GetTimestamp();
        await Parallel.ForAsync(
                0,
                commands,
                new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = cancellationToken },
                async (_, token) => await Seed.CreateMarkerAsync(transport.Client, owner, token).ConfigureAwait(false))
            .ConfigureAwait(false);
        return ThroughputSummary.Of(commands, [Stopwatch.GetElapsedTime(start)]);
    }
}
