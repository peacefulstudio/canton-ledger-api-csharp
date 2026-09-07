// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Commands;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Behavioral parity suite over <see cref="ICantonLedgerClient.CompletionStreamAsync"/>, run against
/// every provider that can stream completions — the in-memory Fake (seeded), gRPC (live) and the JSON
/// transport (live) — through one shared body: submit a command, then drain the completion stream and
/// confirm the submitted command's accepted completion surfaces as a neutral
/// <see cref="CompletionStreamEvent.CommandAccepted"/> the same way regardless of transport.
/// </summary>
/// <remarks>
/// The suite also holds every provider to the termination contract the interface documents, which is
/// transport-neutral by construction and so needs no wire of its own: enumeration may end at any time,
/// possibly having yielded nothing, and a caller that wants to keep following reopens from the highest
/// offset it has observed — which may be the offset it passed in — and supplies its own backoff,
/// because the call may return immediately. Both live transports satisfy it by not ending on their
/// own, so the body's own criterion is what stops each drain.
/// <para>
/// A live lane allocates its party moments before it subscribes, so a window can be answered with a
/// terminal <see cref="CompletionStreamEvent.StreamError"/> whose
/// <see cref="CompletionStreamEvent.StreamError.ErrorId"/> is <c>STALE_STREAM_AUTHORIZATION</c>: the
/// participant opened it against a topology snapshot the allocation had already moved past, and asks
/// for a quick retry. The body does what <see cref="ICantonLedgerClient.CompletionStreamAsync"/>
/// leaves to the caller — reopen from the highest offset observed, backing off between a bounded
/// number of attempts — and reports the reopened window as the drain's ending. Every other stream
/// error stays the ending it is, one sharing this one's status included, because the code is what
/// tells a self-clearing condition from a fault a reopen reproduces.
/// </para>
/// </remarks>
public abstract class LedgerCompletionParityTests
{
    private const string StaleStreamAuthorization = "STALE_STREAM_AUTHORIZATION";
    private const int StaleAuthorizationReopenAttempts = 4;

    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReopenBudget = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StaleAuthorizationBackoff = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Opens a lane that has already submitted a command over this provider's
    /// <see cref="ICantonLedgerClient"/>, exposing the machinery the shared body needs to drain its
    /// completion.
    /// </summary>
    protected abstract Task<CapabilityLane<CompletionProbe>> OpenCompletionAsync(CancellationToken cancellationToken);

    [Fact]
    public async Task CompletionStreamAsync_surfaces_the_submitted_commands_accepted_completion()
    {
        await using var lane = await OpenCompletionAsync(TestContext.Current.CancellationToken);
        var probe = lane.Capability;

        var follow = await FollowAsync(
            probe, probe.BeginExclusiveOffset, int.MaxValue, DrainTimeout, TestContext.Current.CancellationToken);

        follow.Accepted.Should().NotBeNull(
            "the submitted command's completion has to reach the stream, and this drain saw {0}",
            follow.Describe());
        follow.Accepted!.UpdateId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CompletionStreamAsync_ends_the_follow_loop_without_a_terminal_event()
    {
        await using var lane = await OpenCompletionAsync(TestContext.Current.CancellationToken);
        var probe = lane.Capability;

        var follow = await FollowAsync(
            probe, probe.BeginExclusiveOffset, int.MaxValue, DrainTimeout, TestContext.Current.CancellationToken);

        follow.Accepted.Should().NotBeNull(
            "the submitted command's completion has to reach the stream, and this drain saw {0}",
            follow.Describe());
        follow.RanOutOfBudget.Should().BeFalse(
            "the caller's own criterion ends the enumeration, not a deadline it had to impose");
        follow.Observed.Should().NotContain(
            streamEvent => streamEvent is CompletionStreamEvent.StreamError,
            "an ordinary ending carries no closing entry the caller has to wait for");
    }

    [Fact]
    public async Task CompletionStreamAsync_reopening_from_the_highest_observed_offset_loses_no_completion()
    {
        await using var lane = await OpenCompletionAsync(TestContext.Current.CancellationToken);
        var probe = lane.Capability;

        var firstWindow = await FollowAsync(
            probe, probe.BeginExclusiveOffset, maxEvents: 1, DrainTimeout, TestContext.Current.CancellationToken);

        firstWindow.HighestObservedOffset.Should().BeGreaterThanOrEqualTo(
            probe.BeginExclusiveOffset,
            "the resume offset is the highest offset observed, which may still be the one the caller passed in");

        var reopened = await FollowAsync(
            probe,
            firstWindow.HighestObservedOffset,
            int.MaxValue,
            DrainTimeout,
            TestContext.Current.CancellationToken);

        (firstWindow.Accepted ?? reopened.Accepted).Should().NotBeNull(
            "reopening from the highest observed offset must not skip the completion the first window stopped short of");
    }

    [Fact]
    public async Task CompletionStreamAsync_treats_a_window_that_yields_nothing_as_an_ordinary_ending()
    {
        await using var lane = await OpenCompletionAsync(TestContext.Current.CancellationToken);
        var probe = lane.Capability;

        var drained = await FollowAsync(
            probe, probe.BeginExclusiveOffset, int.MaxValue, DrainTimeout, TestContext.Current.CancellationToken);
        drained.Accepted.Should().NotBeNull(
            "the submitted command's completion has to reach the stream, and this drain saw {0}",
            drained.Describe());

        var reopened = await FollowAsync(
            probe, drained.HighestObservedOffset, int.MaxValue, ReopenBudget, TestContext.Current.CancellationToken);

        reopened.Observed.Should().NotContain(
            streamEvent => streamEvent is CompletionStreamEvent.StreamError,
            "a window the caller leaves having seen nothing new is an ending, not a fault");
        reopened.HighestObservedOffset.Should().BeGreaterThanOrEqualTo(
            drained.HighestObservedOffset,
            "an empty window leaves the resume offset exactly where the caller last observed it");
    }

    private static async Task<CompletionFollow> FollowAsync(
        CompletionProbe probe,
        long fromOffset,
        int maxEvents,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        var follow = await DrainOneWindowAsync(probe, fromOffset, maxEvents, budget, cancellationToken);

        while (follow.EndedOnStaleAuthorization
            && follow.StaleAuthorizationReopens < StaleAuthorizationReopenAttempts)
        {
            await Task.Delay(StaleAuthorizationBackoff, cancellationToken);
            follow = follow.ContinuedBy(await DrainOneWindowAsync(
                probe, follow.HighestObservedOffset, maxEvents, budget, cancellationToken));
        }

        return follow;
    }

    private static async Task<CompletionFollow> DrainOneWindowAsync(
        CompletionProbe probe,
        long fromOffset,
        int maxEvents,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        using var windowBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        windowBudget.CancelAfter(budget);

        var observed = new List<CompletionStreamEvent>();
        var highestObservedOffset = fromOffset;
        CompletionStreamEvent.CommandAccepted? accepted = null;

        try
        {
            await foreach (var streamEvent in probe.Client.CompletionStreamAsync(
                probe.Submitter, fromOffset, windowBudget.Token))
            {
                observed.Add(streamEvent);

                if (OffsetOf(streamEvent) is { } offset)
                {
                    highestObservedOffset = Math.Max(highestObservedOffset, offset);
                }

                if (streamEvent is CompletionStreamEvent.CommandAccepted candidate
                    && candidate.Completion.CommandId.Value == probe.ExpectedCommandId.Value)
                {
                    accepted = candidate;
                    break;
                }

                if (observed.Count >= maxEvents)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
            when (windowBudget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new CompletionFollow(observed, highestObservedOffset, accepted, RanOutOfBudget: true);
        }

        return new CompletionFollow(
            observed, highestObservedOffset, accepted, RanOutOfBudget: false);
    }

    private static long? OffsetOf(CompletionStreamEvent streamEvent) => streamEvent switch
    {
        CompletionStreamEvent.CommandAccepted accepted => accepted.Completion.Offset,
        CompletionStreamEvent.CommandRejected rejected => rejected.Completion.Offset,
        CompletionStreamEvent.Checkpoint checkpoint => checkpoint.Offset,
        _ => null,
    };

    private sealed record CompletionFollow(
        IReadOnlyList<CompletionStreamEvent> Observed,
        long HighestObservedOffset,
        CompletionStreamEvent.CommandAccepted? Accepted,
        bool RanOutOfBudget,
        int StaleAuthorizationReopens = 0)
    {
        internal bool EndedOnStaleAuthorization =>
            Observed is [.., CompletionStreamEvent.StreamError { ErrorId: StaleStreamAuthorization }];

        internal CompletionFollow ContinuedBy(CompletionFollow reopened) => new(
            [.. Observed.Where(IsNotStaleAuthorization), .. reopened.Observed],
            Math.Max(HighestObservedOffset, reopened.HighestObservedOffset),
            Accepted ?? reopened.Accepted,
            reopened.RanOutOfBudget,
            StaleAuthorizationReopens + 1);

        internal string Describe() =>
            (Observed.Count == 0
                ? "no event at all"
                : string.Join(", ", Observed.Select(Describe)))
            + $" (ran out of budget: {RanOutOfBudget}, stale-authorization reopens: "
            + $"{StaleAuthorizationReopens})";

        private static bool IsNotStaleAuthorization(CompletionStreamEvent streamEvent) =>
            streamEvent is not CompletionStreamEvent.StreamError { ErrorId: StaleStreamAuthorization };

        private static string Describe(CompletionStreamEvent streamEvent) => streamEvent switch
        {
            CompletionStreamEvent.StreamError { ErrorId: null } fault =>
                $"StreamError({fault.StatusCode}: {fault.Message})",
            CompletionStreamEvent.StreamError error =>
                $"StreamError({error.StatusCode} {error.ErrorId}: {error.Message})",
            _ => streamEvent.GetType().Name,
        };
    }
}

/// <summary>
/// The state a completion-parity lane hands the shared body: the client under test, the submitter to
/// stream completions for, the offset captured before the command was submitted, and the effective
/// command id whose accepted completion the body waits for.
/// </summary>
/// <param name="Client">The client whose completion stream the body drains.</param>
/// <param name="Submitter">The submitter parties to stream completions for.</param>
/// <param name="BeginExclusiveOffset">The offset captured before submitting, so the completion is not
/// missed.</param>
/// <param name="ExpectedCommandId">The effective command id the submitted command recorded.</param>
public sealed record CompletionProbe(
    ICantonLedgerClient Client,
    SubmitterInfo Submitter,
    long BeginExclusiveOffset,
    CommandId ExpectedCommandId);
