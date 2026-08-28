// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Richtypes;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class FakeLedgerCompletionParityTests : LedgerCompletionParityTests
{
    protected override async Task<CapabilityLane<CompletionProbe>> OpenCompletionAsync(CancellationToken cancellationToken)
    {
        var owner = new Party("fake::completion-owner");
        var expectedCommandId = new CommandId("fake-completion-cmd");
        var completion = new Completion(
            expectedCommandId,
            Offset: 1,
            ActAs: [owner],
            SynchronizerTime: new SynchronizerTime("fake::sync-1", DateTimeOffset.UnixEpoch),
            SubmissionId: null,
            UserId: null,
            DeduplicationOffset: null,
            DeduplicationDuration: null);

        var client = FakeLedgerClient.Create()
            .WithCompletionEvents(
                new CompletionStreamEvent.Checkpoint(0),
                new CompletionStreamEvent.CommandAccepted(completion, "fake-update-1"))
            .Build();

        var submission = CommandsSubmission
            .Single(CreateCommand.For(new Marker(owner)))
            .WithActAs(owner)
            .WithCommandId(expectedCommandId);
        var returnedCommandId = await client.SubmitAsync(submission, cancellationToken);

        var probe = new CompletionProbe(client, owner, BeginExclusiveOffset: 0, returnedCommandId);
        return new CapabilityLane<CompletionProbe>(probe, client.DisposeAsync);
    }
}
