// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing.Localnet;
using Daml.Runtime.Data;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using RichTypes;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

/// <summary>
/// LocalNet coverage that a command submitted over gRPC has its accepted completion observable on
/// <see cref="ICantonLedgerClient.CompletionStreamAsync"/> from an offset captured before the
/// submission.
/// </summary>
/// <remarks>
/// The party is allocated moments before the stream is opened, so a window can be answered with a
/// terminal <see cref="CompletionStreamEvent.StreamError"/> whose
/// <see cref="CompletionStreamEvent.StreamError.ErrorId"/> is <c>STALE_STREAM_AUTHORIZATION</c>: the
/// participant opened it against a topology snapshot the allocation had already moved past, and asks
/// for a quick retry. The drain does what <see cref="ICantonLedgerClient.CompletionStreamAsync"/>
/// leaves to the caller — reopen from the highest offset observed, backing off between a bounded
/// number of attempts. Every other stream error ends the drain, because the code is what tells a
/// self-clearing condition from a fault a reopen reproduces.
/// </remarks>
[Trait("Category", "Integration")]
public class CompletionStreamRoundTripTests
{
    private const string StaleStreamAuthorization = "STALE_STREAM_AUTHORIZATION";
    private const int StaleAuthorizationReopenAttempts = 4;

    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StaleAuthorizationBackoff = TimeSpan.FromSeconds(2);

    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this integration test.";

    private static string DarPath() => Path.Combine(
        AppContext.BaseDirectory, "testdata", "richtypes", "richtypes.dar");

    [Fact]
    public async Task Submit_completion_is_observed_on_CompletionStreamAsync_from_pre_submit_offset()
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        await using var fixture = LocalnetFixture.FromEnvironment();

        var darOutcome = await fixture.UploadDarAsync(DarPath(), TestContext.Current.CancellationToken);
        Assert.True(
            darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
            $"Unexpected DAR upload outcome: {darOutcome}");

        var party = await fixture.AllocatePartyAsync("cdg", cancellationToken: TestContext.Current.CancellationToken);
        var owner = new Party(party.PartyId);
        var userId = fixture.ValidatorUserId;
        await using var actAsRights = ActAsRightsLease.ForValidator(fixture);
        await actAsRights.GrantAsync(party.PartyId, TestContext.Current.CancellationToken);

        await using var services = LocalnetLedgerServices.ForValidator(fixture, userId);
        var client = services.GetRequiredService<ICantonLedgerClient>();

        var preSubmitOffset = (await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken)).Value;

        var commandId = Guid.NewGuid().ToString();
        var submission = RuntimeCommands.CommandsSubmission
            .Single(RuntimeCommands.CreateCommand.For(new Marker(owner)))
            .WithActAs(owner)
            .WithCommandId(new RuntimeCommands.CommandId(commandId));

        var returnedCommandId = await client.SubmitAsync(submission, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(commandId, returnedCommandId.Value);

        var accepted = await ObserveAcceptedAsync(
            client, owner, preSubmitOffset, commandId, TestContext.Current.CancellationToken);

        Assert.NotNull(accepted);
        Assert.Equal(commandId, accepted!.Completion.CommandId.Value);
        Assert.False(string.IsNullOrWhiteSpace(accepted.UpdateId), "an accepted completion carries an update id");
    }

    private static async Task<CompletionStreamEvent.CommandAccepted?> ObserveAcceptedAsync(
        ICantonLedgerClient client,
        Party owner,
        long beginExclusiveOffset,
        string commandId,
        CancellationToken cancellationToken)
    {
        var fromOffset = beginExclusiveOffset;
        for (var attempt = 0; attempt <= StaleAuthorizationReopenAttempts; attempt++)
        {
            var window = await DrainOneWindowAsync(client, owner, fromOffset, commandId, cancellationToken);
            if (window.Accepted is not null || !window.EndedOnStaleAuthorization)
            {
                return window.Accepted;
            }

            fromOffset = window.HighestObservedOffset;
            await Task.Delay(StaleAuthorizationBackoff, cancellationToken);
        }

        return null;
    }

    private static async Task<CompletionWindow> DrainOneWindowAsync(
        ICantonLedgerClient client,
        Party owner,
        long fromOffset,
        string commandId,
        CancellationToken cancellationToken)
    {
        using var windowBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        windowBudget.CancelAfter(DrainTimeout);

        var highestObservedOffset = fromOffset;
        var endedOnStaleAuthorization = false;

        await foreach (var streamEvent in client.CompletionStreamAsync(owner, fromOffset, windowBudget.Token))
        {
            if (OffsetOf(streamEvent) is { } offset)
            {
                highestObservedOffset = Math.Max(highestObservedOffset, offset);
            }

            if (streamEvent is CompletionStreamEvent.CommandAccepted accepted
                && accepted.Completion.CommandId.Value == commandId)
            {
                return new CompletionWindow(accepted, highestObservedOffset, EndedOnStaleAuthorization: false);
            }

            endedOnStaleAuthorization =
                streamEvent is CompletionStreamEvent.StreamError { ErrorId: StaleStreamAuthorization };
        }

        return new CompletionWindow(null, highestObservedOffset, endedOnStaleAuthorization);
    }

    private static long? OffsetOf(CompletionStreamEvent streamEvent) => streamEvent switch
    {
        CompletionStreamEvent.CommandAccepted accepted => accepted.Completion.Offset,
        CompletionStreamEvent.CommandRejected rejected => rejected.Completion.Offset,
        CompletionStreamEvent.Checkpoint checkpoint => checkpoint.Offset,
        _ => null,
    };

    private sealed record CompletionWindow(
        CompletionStreamEvent.CommandAccepted? Accepted,
        long HighestObservedOffset,
        bool EndedOnStaleAuthorization);
}
