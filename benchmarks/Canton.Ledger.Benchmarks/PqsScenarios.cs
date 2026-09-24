// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Diagnostics;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Canton.Ledger.Benchmarks;

internal static class PqsScenarios
{
    private static readonly TimeSpan ProjectionPollInterval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan ProjectionTimeout = TimeSpan.FromMinutes(5);

    public static async Task<LatencySummary> ProjectionLagAsync(
        IPqsClient pqs, Transport writer, Party issuer, int samples, CancellationToken cancellationToken)
    {
        var lags = new List<TimeSpan>(samples);
        for (var i = 0; i < samples; i++)
        {
            var start = Stopwatch.GetTimestamp();
            var contractId = await Seed.CreateAssetAsync(writer.Client, issuer, cancellationToken).ConfigureAwait(false);
            await AwaitProjectionAsync(pqs, contractId, cancellationToken).ConfigureAwait(false);
            lags.Add(Stopwatch.GetElapsedTime(start));
        }

        return LatencySummary.Of(lags);
    }

    public static async Task<ThroughputSummary> PagedQueryAsync(
        IPqsClient pqs,
        Transport writer,
        Party issuer,
        int rows,
        int concurrency,
        int repetitions,
        CancellationToken cancellationToken)
    {
        var seeded = new ConcurrentBag<ContractId<Asset>>();
        await Parallel.ForAsync(
                0,
                rows,
                new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = cancellationToken },
                async (_, token) => seeded.Add(await Seed.CreateAssetAsync(writer.Client, issuer, token).ConfigureAwait(false)))
            .ConfigureAwait(false);
        foreach (var contractId in seeded)
        {
            await AwaitProjectionAsync(pqs, contractId, cancellationToken).ConfigureAwait(false);
        }

        var page = new PqsPage(rows);
        var elapsed = new List<TimeSpan>(repetitions);
        for (var repetition = 0; repetition < repetitions; repetition++)
        {
            var start = Stopwatch.GetTimestamp();
            var contracts = await pqs.QueryAsync<Asset>(page, cancellationToken).ConfigureAwait(false);
            elapsed.Add(Stopwatch.GetElapsedTime(start));
            if (contracts.Count != rows)
            {
                throw new InvalidOperationException($"PQS page returned {contracts.Count} rows where {rows} were requested");
            }
        }

        return ThroughputSummary.Of(rows, elapsed);
    }

    private static async Task AwaitProjectionAsync(
        IPqsClient pqs, ContractId<Asset> contractId, CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(ProjectionTimeout.TotalSeconds * Stopwatch.Frequency);
        while (!await pqs.ExistsAsync(contractId, cancellationToken).ConfigureAwait(false))
        {
            if (Stopwatch.GetTimestamp() > deadline)
            {
                throw new TimeoutException($"contract {contractId.Value} was not projected into PQS within {ProjectionTimeout}");
            }

            await Task.Delay(ProjectionPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
