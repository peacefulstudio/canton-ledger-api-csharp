// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Benchmarks;

internal sealed record LatencySummary(int Samples, double MeanMs, double P50Ms, double P95Ms, double P99Ms, double MaxMs)
{
    public static LatencySummary Of(IReadOnlyCollection<TimeSpan> samples)
    {
        if (samples.Count == 0)
        {
            throw new InvalidOperationException("a latency summary needs at least one sample");
        }

        var sortedMs = samples.Select(sample => sample.TotalMilliseconds).Order().ToArray();
        return new LatencySummary(
            sortedMs.Length,
            sortedMs.Average(),
            NearestRank(sortedMs, 0.50),
            NearestRank(sortedMs, 0.95),
            NearestRank(sortedMs, 0.99),
            sortedMs[^1]);
    }

    private static double NearestRank(double[] sortedMs, double percentile)
    {
        var rank = (int)Math.Ceiling(percentile * sortedMs.Length);
        return sortedMs[Math.Clamp(rank - 1, 0, sortedMs.Length - 1)];
    }
}
