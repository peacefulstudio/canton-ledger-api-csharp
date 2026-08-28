// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Commands;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Behavioral parity suite over <see cref="ICantonLedgerClient.CompletionStreamAsync"/>, run against
/// every provider that can stream completions — the in-memory Fake (seeded) and gRPC (live) — through
/// one shared body: submit a command, then drain the completion stream and confirm the submitted
/// command's accepted completion surfaces as a neutral
/// <see cref="CompletionStreamEvent.CommandAccepted"/> the same way regardless of transport.
/// </summary>
/// <remarks>
/// REST does not join this suite yet. <c>RestLedgerClient.CompletionStreamAsync</c> does serve
/// completions — one bounded window per <c>POST /v2/commands/completions</c> call — so the exclusion
/// is no longer that it has no stream to place under parity; what it still needs is a live lane that
/// submits over REST and drains that window before it closes, which is follow-up work rather than a
/// transport gap.
/// </remarks>
public abstract class LedgerCompletionParityTests
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(30);

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

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(DrainTimeout);

        CompletionStreamEvent.CommandAccepted? accepted = null;
        await foreach (var streamEvent in probe.Client.CompletionStreamAsync(
            probe.Submitter, probe.BeginExclusiveOffset, cts.Token))
        {
            if (streamEvent is CompletionStreamEvent.CommandAccepted candidate
                && candidate.Completion.CommandId.Value == probe.ExpectedCommandId.Value)
            {
                accepted = candidate;
                break;
            }
        }

        accepted.Should().NotBeNull();
        accepted!.UpdateId.Should().NotBeNullOrWhiteSpace();
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
