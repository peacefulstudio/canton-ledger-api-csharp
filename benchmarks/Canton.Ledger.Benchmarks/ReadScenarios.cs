// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Benchmarks;

internal static class ReadScenarios
{
    public static async Task<ThroughputSummary> UpdateStreamReplayAsync(
        Transport transport,
        Party owner,
        LedgerOffset fromOffset,
        LedgerOffset toOffset,
        int expectedCreates,
        int repetitions,
        CancellationToken cancellationToken)
    {
        var elapsed = new List<TimeSpan>(repetitions);
        for (var repetition = 0; repetition < repetitions; repetition++)
        {
            var creates = 0;
            var start = Stopwatch.GetTimestamp();
            await foreach (var streamEvent in transport.Client
                               .SubscribeAsync<Marker>(owner, fromOffset, toOffset, cancellationToken)
                               .ConfigureAwait(false))
            {
                switch (streamEvent)
                {
                    case ContractStreamEvent<Marker>.Created:
                        creates++;
                        break;
                    case ContractStreamEvent<Marker>.StreamError error:
                        throw new InvalidOperationException(
                            $"{transport.Name} update stream failed: {error.StatusCode} {error.ErrorId} {error.Message}");
                }
            }

            elapsed.Add(Stopwatch.GetElapsedTime(start));
            ExpectCount(transport, "update stream replay", expectedCreates, creates);
        }

        return ThroughputSummary.Of(expectedCreates, elapsed);
    }

    public static async Task<ThroughputSummary> ActiveContractSnapshotAsync(
        Transport transport,
        Party owner,
        LedgerOffset activeAtOffset,
        int expectedContracts,
        int repetitions,
        CancellationToken cancellationToken)
    {
        var elapsed = new List<TimeSpan>(repetitions);
        for (var repetition = 0; repetition < repetitions; repetition++)
        {
            var contracts = 0;
            var start = Stopwatch.GetTimestamp();
            await foreach (var entry in transport.Client
                               .SubscribeActiveAsync<Marker>(owner, activeAtOffset, cancellationToken)
                               .ConfigureAwait(false))
            {
                switch (entry)
                {
                    case AcsSnapshotEntry<Marker>.Created:
                        contracts++;
                        break;
                    case AcsSnapshotEntry<Marker>.StreamError error:
                        throw new InvalidOperationException(
                            $"{transport.Name} ACS snapshot failed: {error.StatusCode} {error.ErrorId} {error.Message}");
                }
            }

            elapsed.Add(Stopwatch.GetElapsedTime(start));
            ExpectCount(transport, "ACS snapshot", expectedContracts, contracts);
        }

        return ThroughputSummary.Of(expectedContracts, elapsed);
    }

    private static void ExpectCount(Transport transport, string scenario, int expected, int actual)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"{transport.Name} {scenario} read {actual} contracts where {expected} were seeded");
        }
    }
}
