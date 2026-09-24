// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Benchmarks;

internal static class TailScenarios
{
    private const int WarmupSamples = 3;
    private static readonly TimeSpan ArrivalTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ReopenBackoff = TimeSpan.FromSeconds(1);

    public static async Task<TailLatency> UpdateTailAsync(
        Transport streamer,
        Transport writer,
        Party owner,
        int samples,
        TimeSpan spacing,
        CancellationToken cancellationToken)
    {
        var board = new ArrivalBoard();
        var fromOffset = await streamer.Client.GetLedgerEndAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var reopens = new StrongBox<int>();
        var tail = Task.Run(() => FollowUpdatesAsync(streamer, owner, fromOffset, board, reopens, stop.Token), stop.Token);

        try
        {
            var (delivery, submit) = await SampleAsync(
                    samples,
                    spacing,
                    async token => (await Seed.CreateMarkerAsync(writer.Client, owner, token).ConfigureAwait(false)).Value,
                    board,
                    cancellationToken)
                .ConfigureAwait(false);
            await stop.CancelAsync().ConfigureAwait(false);
            await StoppedAsync(tail).ConfigureAwait(false);
            return new TailLatency(delivery, submit, reopens.Value);
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
        }
    }

    public static async Task<TailLatency> CompletionTailAsync(
        Transport transport,
        Party owner,
        int samples,
        TimeSpan spacing,
        CancellationToken cancellationToken)
    {
        var board = new ArrivalBoard();
        var fromOffset = await transport.Client.GetLedgerEndAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var reopens = new StrongBox<int>();
        var tail = Task.Run(() => FollowCompletionsAsync(transport, owner, fromOffset.Value, board, reopens, stop.Token), stop.Token);

        try
        {
            var (delivery, submit) = await SampleAsync(
                    samples,
                    spacing,
                    async token =>
                    {
                        var commandId = Guid.NewGuid().ToString("N");
                        var submission = RuntimeCommands.CommandsSubmission
                            .Single(RuntimeCommands.CreateCommand.For(new Marker(owner)))
                            .WithActAs(owner)
                            .WithCommandId(new RuntimeCommands.CommandId(commandId));
                        await transport.Client.SubmitAsync(submission, cancellationToken: token).ConfigureAwait(false);
                        return commandId;
                    },
                    board,
                    cancellationToken)
                .ConfigureAwait(false);
            await stop.CancelAsync().ConfigureAwait(false);
            await StoppedAsync(tail).ConfigureAwait(false);
            return new TailLatency(delivery, submit, reopens.Value);
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
        }
    }

    private static async Task<(LatencySummary Delivery, LatencySummary Submit)> SampleAsync(
        int samples,
        TimeSpan spacing,
        Func<CancellationToken, Task<string>> submit,
        ArrivalBoard board,
        CancellationToken cancellationToken)
    {
        var delivery = new List<TimeSpan>(samples);
        var submitted = new List<TimeSpan>(samples);
        for (var i = 0; i < WarmupSamples + samples; i++)
        {
            var start = Stopwatch.GetTimestamp();
            var key = await submit(cancellationToken).ConfigureAwait(false);
            var submitReturned = Stopwatch.GetTimestamp();
            var arrived = await board.ArrivalOf(key).WaitAsync(ArrivalTimeout, cancellationToken).ConfigureAwait(false);
            if (i >= WarmupSamples)
            {
                delivery.Add(Stopwatch.GetElapsedTime(start, arrived));
                submitted.Add(Stopwatch.GetElapsedTime(start, submitReturned));
            }

            await Task.Delay(spacing, cancellationToken).ConfigureAwait(false);
        }

        return (LatencySummary.Of(delivery), LatencySummary.Of(submitted));
    }

    private static async Task FollowUpdatesAsync(
        Transport streamer,
        Party owner,
        LedgerOffset fromOffset,
        ArrivalBoard board,
        StrongBox<int> reopens,
        CancellationToken stop)
    {
        var resumeFrom = fromOffset;
        while (!stop.IsCancellationRequested)
        {
            await foreach (var streamEvent in streamer.Client
                               .SubscribeAsync<Marker>(owner, resumeFrom, null, stop)
                               .ConfigureAwait(false))
            {
                switch (streamEvent)
                {
                    case ContractStreamEvent<Marker>.Created created:
                        board.Arrived(created.ContractId.Value);
                        resumeFrom = created.Offset;
                        break;
                    case ContractStreamEvent<Marker>.Checkpoint checkpoint:
                        resumeFrom = checkpoint.Offset;
                        break;
                    case ContractStreamEvent<Marker>.StreamError:
                        reopens.Value++;
                        break;
                }
            }

            await Task.Delay(ReopenBackoff, stop).ConfigureAwait(false);
        }
    }

    private static async Task FollowCompletionsAsync(
        Transport transport,
        Party owner,
        long fromOffset,
        ArrivalBoard board,
        StrongBox<int> reopens,
        CancellationToken stop)
    {
        var resumeFrom = fromOffset;
        while (!stop.IsCancellationRequested)
        {
            await foreach (var streamEvent in transport.Client
                               .CompletionStreamAsync(owner, resumeFrom, stop)
                               .ConfigureAwait(false))
            {
                switch (streamEvent)
                {
                    case CompletionStreamEvent.CommandAccepted accepted:
                        board.Arrived(accepted.Completion.CommandId.Value);
                        resumeFrom = Math.Max(resumeFrom, accepted.Completion.Offset);
                        break;
                    case CompletionStreamEvent.CommandRejected rejected:
                        resumeFrom = Math.Max(resumeFrom, rejected.Completion.Offset);
                        break;
                    case CompletionStreamEvent.Checkpoint checkpoint:
                        resumeFrom = Math.Max(resumeFrom, checkpoint.Offset);
                        break;
                    case CompletionStreamEvent.StreamError:
                        reopens.Value++;
                        break;
                }
            }

            await Task.Delay(ReopenBackoff, stop).ConfigureAwait(false);
        }
    }

    private static async Task StoppedAsync(Task tail)
    {
        try
        {
            await tail.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
