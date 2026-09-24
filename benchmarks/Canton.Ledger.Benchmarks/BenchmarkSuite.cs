// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace Canton.Ledger.Benchmarks;

internal static class BenchmarkSuite
{
    public static async Task<BenchmarkReport> RunAsync(
        BenchmarkLedger ledger, BenchmarkOptions options, CancellationToken cancellationToken)
    {
        var environment = BenchmarkEnvironment.Capture(
            options, await ledger.LedgerApiVersionAsync(cancellationToken).ConfigureAwait(false), ledger.Pqs is not null);
        var latencies = new List<LatencyResult>();
        var throughputs = new List<ThroughputResult>();
        var notes = new List<string>();

        foreach (var transport in ledger.Transports)
        {
            Progress($"warming up {transport.Name}");
            await SubmitScenarios.SequentialLatencyAsync(transport, ledger.Owner, options.WarmupCommands, cancellationToken)
                .ConfigureAwait(false);
        }

        var seedFrom = await ledger.Grpc.Client.GetLedgerEndAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        foreach (var transport in ledger.Transports)
        {
            Progress($"concurrent submit throughput over {transport.Name}");
            throughputs.Add(new ThroughputResult(
                $"Submit-and-wait, {options.Concurrency} in flight",
                transport.Name,
                "commands/s",
                await SubmitScenarios.ConcurrentThroughputAsync(
                        transport, ledger.Owner, options.SeedContractsPerTransport, options.Concurrency, cancellationToken)
                    .ConfigureAwait(false)));
        }

        var seedTo = await ledger.Grpc.Client.GetLedgerEndAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var seeded = options.SeedContractsPerTransport * ledger.Transports.Count;
        var active = seeded + options.WarmupCommands * ledger.Transports.Count;

        foreach (var transport in ledger.Transports)
        {
            Progress($"update stream replay over {transport.Name}");
            throughputs.Add(new ThroughputResult(
                "Update stream replay (bounded range)",
                transport.Name,
                "events/s",
                await ReadScenarios.UpdateStreamReplayAsync(
                        transport, ledger.Owner, seedFrom, seedTo, seeded, options.Repetitions, cancellationToken)
                    .ConfigureAwait(false)));
        }

        Progress($"seeding {options.AcsContracts} contracts for the ACS snapshot party");
        await SubmitScenarios.ConcurrentThroughputAsync(
                ledger.Grpc, ledger.SnapshotOwner, options.AcsContracts, options.Concurrency, cancellationToken)
            .ConfigureAwait(false);
        var snapshotAt = await ledger.Grpc.Client.GetLedgerEndAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        foreach (var transport in ledger.Transports)
        {
            Progress($"ACS snapshot over {transport.Name}");
            throughputs.Add(new ThroughputResult(
                $"Active-contract snapshot, {options.AcsContracts} contracts",
                transport.Name,
                "contracts/s",
                await ReadScenarios.ActiveContractSnapshotAsync(
                        transport, ledger.SnapshotOwner, snapshotAt, options.AcsContracts, options.Repetitions, cancellationToken)
                    .ConfigureAwait(false)));
        }

        foreach (var transport in ledger.Transports)
        {
            Progress($"large ACS snapshot over {transport.Name}");
            throughputs.Add(new ThroughputResult(
                $"Active-contract snapshot, {active} contracts",
                transport.Name,
                "contracts/s",
                await ReadScenarios.ActiveContractSnapshotAsync(
                        transport, ledger.Owner, seedTo, active, options.Repetitions, cancellationToken)
                    .ConfigureAwait(false)));
        }

        foreach (var transport in ledger.Transports)
        {
            Progress($"sequential submit-and-wait latency over {transport.Name}");
            latencies.Add(new LatencyResult(
                "Submit-and-wait (sequential)",
                transport.Name,
                await SubmitScenarios.SequentialLatencyAsync(transport, ledger.Owner, options.LatencySamples, cancellationToken)
                    .ConfigureAwait(false)));
        }

        var spacing = TimeSpan.FromMilliseconds(options.TailSpacingMs);
        IReadOnlyList<Transport> tailTransports = [.. ledger.Transports, .. ledger.RestIdleWindowVariants];
        foreach (var transport in tailTransports)
        {
            Progress($"completion stream latency over {transport.Name}");
            var completion = await TailScenarios.CompletionTailAsync(
                    transport, ledger.Owner, options.TailSamples, spacing, cancellationToken)
                .ConfigureAwait(false);
            latencies.Add(new LatencyResult("Submit → completion on the completion stream", transport.Name, completion.Delivery));
            NoteReopens(notes, "completion stream", transport, completion);
        }

        foreach (var transport in tailTransports)
        {
            Progress($"live update tail latency over {transport.Name}");
            var tail = await TailScenarios.UpdateTailAsync(
                    transport, ledger.Grpc, ledger.Owner, options.TailSamples, spacing, cancellationToken)
                .ConfigureAwait(false);
            latencies.Add(new LatencyResult("Submit → created event on a live update tail", transport.Name, tail.Delivery));
            NoteReopens(notes, "live update tail", transport, tail);
        }

        if (ledger.Pqs is { } pqs)
        {
            Progress("PQS projection lag");
            latencies.Add(new LatencyResult(
                "Submit → contract visible in PQS",
                "PQS",
                await PqsScenarios.ProjectionLagAsync(pqs, ledger.Grpc, ledger.Operator, options.PqsLagSamples, cancellationToken)
                    .ConfigureAwait(false)));

            Progress("PQS paged query");
            throughputs.Add(new ThroughputResult(
                "Paged template query",
                "PQS",
                "rows/s",
                await PqsScenarios.PagedQueryAsync(
                        pqs, ledger.Grpc, ledger.Operator, options.PqsRows, options.Concurrency, options.Repetitions, cancellationToken)
                    .ConfigureAwait(false)));
        }
        else
        {
            notes.Add("PQS was not measured: CANTON_LOCALNET_A_VALIDATOR_1_PQS_CONNECTION_STRING was not set.");
        }

        if (Seed.Retries > 0)
        {
            notes.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{Seed.Retries} create(s) were rejected with a retryable category (ContentionOnSharedResources or TransientServerFailure) and retried with exponential backoff; the retry time is inside the measured figures. On LocalNet this is the validator running out of synchronizer traffic between top-ups (SEQUENCER_NOT_ENOUGH_TRAFFIC_CREDIT)."));
        }

        return new BenchmarkReport(environment, options, latencies, throughputs, notes);
    }

    private static void NoteReopens(List<string> notes, string stream, Transport transport, TailLatency tail)
    {
        if (tail.StreamReopens > 0)
        {
            notes.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"The {transport.Name} {stream} ended with an in-band StreamError {tail.StreamReopens} time(s) and was reopened from its last offset."));
        }
    }

    private static void Progress(string step) =>
        Console.Error.WriteLine($"[{DateTimeOffset.UtcNow:HH:mm:ss}] {step}");
}
