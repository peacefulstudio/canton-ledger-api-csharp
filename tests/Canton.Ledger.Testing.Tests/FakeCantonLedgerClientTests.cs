// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class FakeCantonLedgerClientTests
{
    private static readonly Party Alice = new("alice");

    private static Completion CompletionFor(string commandId, long offset) => new(
        new CommandId(commandId),
        offset,
        new[] { Alice },
        new SynchronizerTime("sync-1", DateTimeOffset.UnixEpoch),
        SubmissionId: null,
        UserId: null,
        DeduplicationOffset: null,
        DeduplicationDuration: null);

    private static CommandsSubmission SubmissionCreatingDemoAsset() => CommandsSubmission
        .Single(CreateCommand.For(new DemoAsset(Alice, Alice, "GOLD", 1m)))
        .WithActAs(Alice);

    private static TransactionTree TreeWithOneCreate() => new(
        "update-1",
        LedgerOffset.At(1),
        [
            new TreeEvent.Created(
                EventId: "1",
                ContractId: "00cid",
                TemplateId: DemoAsset.TemplateId,
                CreateArguments: new DemoAsset(Alice, Alice, "GOLD", 1m).ToRecord(),
                WitnessParties: [Alice],
                Signatories: [Alice],
                Observers: [],
                ContractKey: null,
                CreatedAt: DateTimeOffset.UnixEpoch),
        ]);

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        var items = new List<T>();
        await foreach (var item in source)
        {
            items.Add(item);
        }

        return items;
    }

    [Fact]
    public void FakeLedgerClient_implements_the_Canton_participant_surface()
    {
        FakeLedgerClient.Create().Build().Should().BeAssignableTo<ICantonLedgerClient>();
    }

    [Fact]
    public async Task CompletionStreamAsync_replays_staged_completion_events_in_order()
    {
        var accepted = new CompletionStreamEvent.CommandAccepted(CompletionFor("cmd-1", 7), "update-1");
        var checkpoint = new CompletionStreamEvent.Checkpoint(8);
        var rejected = new CompletionStreamEvent.CommandRejected(CompletionFor("cmd-2", 9), new CompletionStatus(3, "boom"));
        var client = FakeLedgerClient.Create().WithCompletionEvents(accepted, checkpoint, rejected).Build();

        var events = await CollectAsync(client.CompletionStreamAsync(Alice, cancellationToken: TestContext.Current.CancellationToken));

        events.Should().Equal(accepted, checkpoint, rejected);
    }

    [Fact]
    public void CompletionStreamAsync_without_staged_events_throws_descriptive_NotSupportedException()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.CompletionStreamAsync(Alice);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*WithCompletionEvents*")
            .WithMessage("*CompletionStreamAsync*");
    }

    [Fact]
    public async Task CompletionStreamAsync_with_an_empty_staged_list_drains_to_nothing()
    {
        var client = FakeLedgerClient.Create().WithCompletionEvents().Build();

        var events = await CollectAsync(client.CompletionStreamAsync(Alice, cancellationToken: TestContext.Current.CancellationToken));

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitAsync_echoes_the_submissions_command_id_when_one_is_set()
    {
        var client = FakeLedgerClient.Create().Build();
        var submission = CommandsSubmission
            .Single(CreateCommand.For(new DemoAsset(Alice, Alice, "GOLD", 1m)))
            .WithActAs(Alice)
            .WithCommandId(new CommandId("cmd-echo"));

        var commandId = await client.SubmitAsync(submission, TestContext.Current.CancellationToken);

        commandId.Value.Should().Be("cmd-echo");
    }

    [Fact]
    public async Task SubmitAsync_mints_a_command_id_when_the_submission_omits_one()
    {
        var client = FakeLedgerClient.Create().Build();
        var submission = CommandsSubmission
            .Single(CreateCommand.For(new DemoAsset(Alice, Alice, "GOLD", 1m)))
            .WithActAs(Alice);

        var commandId = await client.SubmitAsync(submission, TestContext.Current.CancellationToken);

        commandId.Value.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task SubmitReassignmentAsync_echoes_the_submissions_command_id_when_one_is_set()
    {
        var client = FakeLedgerClient.Create().Build();
        var submission = ReassignmentSubmission
            .Of(new UnassignCommand("00cid", (SynchronizerId)"src", (SynchronizerId)"tgt"), Alice)
            .WithCommandId(new CommandId("cmd-reassign"));

        var commandId = await client.SubmitReassignmentAsync(submission, TestContext.Current.CancellationToken);

        commandId.Value.Should().Be("cmd-reassign");
    }

    [Fact]
    public async Task TrySubmitAndWaitForReassignmentAsync_returns_the_staged_outcome()
    {
        var unassigned = ContractEvents.Unassigned<DemoAsset>(
            new ContractId<DemoAsset>("00cid"), LedgerOffset.At(1), (SynchronizerId)"src", (SynchronizerId)"tgt", "reassign-1", 0, new[] { Alice });
        var outcome = LedgerOutcomes.One(unassigned);
        var client = FakeLedgerClient.Create().WithReassignmentResult(outcome).Build();
        var submission = ReassignmentSubmission.Of(new UnassignCommand("00cid", (SynchronizerId)"src", (SynchronizerId)"tgt"), Alice);

        var result = await client.TrySubmitAndWaitForReassignmentAsync<DemoAsset>(
            submission, cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeSameAs(outcome);
    }

    [Fact]
    public async Task GetLedgerEndAsync_advances_by_one_offset_across_a_committed_reassignment()
    {
        var unassigned = ContractEvents.Unassigned<DemoAsset>(
            new ContractId<DemoAsset>("00cid"), LedgerOffset.At(1), (SynchronizerId)"src", (SynchronizerId)"tgt", "reassign-1", 0, new[] { Alice });
        var client = FakeLedgerClient.Create()
            .WithLedgerEnd(LedgerOffset.At(42))
            .WithReassignmentResult(LedgerOutcomes.One(unassigned))
            .Build();
        var submission = ReassignmentSubmission.Of(new UnassignCommand("00cid", (SynchronizerId)"src", (SynchronizerId)"tgt"), Alice);

        await client.TrySubmitAndWaitForReassignmentAsync<DemoAsset>(
            submission, cancellationToken: TestContext.Current.CancellationToken);

        var end = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        end.Should().Be(LedgerOffset.At(43));
    }

    [Fact]
    public async Task TrySubmitAndWaitForReassignmentAsync_for_an_unstaged_type_throws_descriptive_NotSupportedException()
    {
        var client = FakeLedgerClient.Create().Build();
        var submission = ReassignmentSubmission.Of(new UnassignCommand("00cid", (SynchronizerId)"src", (SynchronizerId)"tgt"), Alice);

        var act = () => client.TrySubmitAndWaitForReassignmentAsync<DemoAsset>(submission);

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithReassignmentResult").And.Contain("DemoAsset");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionTreeAsync_returns_the_staged_outcome()
    {
        var outcome = LedgerOutcomes.One(TreeWithOneCreate());
        var client = FakeLedgerClient.Create().WithTransactionTree(outcome).Build();

        var result = await client.TrySubmitAndWaitForTransactionTreeAsync(
            SubmissionCreatingDemoAsset(), Alice, cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeSameAs(outcome);
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionTreeAsync_without_a_staged_outcome_throws_descriptive_NotSupportedException()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.TrySubmitAndWaitForTransactionTreeAsync(SubmissionCreatingDemoAsset(), Alice);

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithTransactionTree")
            .And.Contain("TrySubmitAndWaitForTransactionTreeAsync");
    }

    [Fact]
    public async Task GetLedgerEndAsync_advances_by_one_offset_across_a_committed_transaction_tree()
    {
        var client = FakeLedgerClient.Create()
            .WithLedgerEnd(LedgerOffset.At(42))
            .WithTransactionTree(LedgerOutcomes.One(TreeWithOneCreate()))
            .Build();

        await client.TrySubmitAndWaitForTransactionTreeAsync(
            SubmissionCreatingDemoAsset(), Alice, cancellationToken: TestContext.Current.CancellationToken);

        var end = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        end.Should().Be(LedgerOffset.At(43));
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_returns_the_staged_estimate()
    {
        var estimate = new TrafficCostEstimate(DateTimeOffset.UnixEpoch, 1_024, 256, 1_280);
        var client = FakeLedgerClient.Create().WithTrafficCostEstimate(estimate).Build();

        var result = await client.EstimateTrafficCostAsync(
            SubmissionCreatingDemoAsset(), cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeSameAs(estimate);
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_staged_with_no_estimate_replays_a_participant_that_served_none()
    {
        var client = FakeLedgerClient.Create().WithTrafficCostEstimate(null).Build();

        var result = await client.EstimateTrafficCostAsync(
            SubmissionCreatingDemoAsset(), cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_without_a_staged_estimate_throws_descriptive_NotSupportedException()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.EstimateTrafficCostAsync(SubmissionCreatingDemoAsset());

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithTrafficCostEstimate")
            .And.Contain("EstimateTrafficCostAsync");
    }

    [Fact]
    public async Task GetConnectedSynchronizersAsync_returns_the_staged_synchronizers()
    {
        var connected = new ConnectedSynchronizer("alias", "sync-1", SynchronizerPermissionLevel.Submission);
        var client = FakeLedgerClient.Create().WithConnectedSynchronizers(connected).Build();

        var result = await client.GetConnectedSynchronizersAsync(cancellationToken: TestContext.Current.CancellationToken);

        result.Should().ContainSingle().Which.Should().Be(connected);
    }

    [Fact]
    public async Task GetConnectedSynchronizersAsync_without_staged_synchronizers_throws_descriptive_NotSupportedException()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.GetConnectedSynchronizersAsync();

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithConnectedSynchronizers");
    }

    [Fact]
    public async Task GetLedgerApiVersionAsync_returns_the_staged_version()
    {
        var client = FakeLedgerClient.Create().WithLedgerApiVersion("3.5.9").Build();

        var version = await client.GetLedgerApiVersionAsync(TestContext.Current.CancellationToken);

        version.Should().Be("3.5.9");
    }

    [Fact]
    public async Task GetLedgerApiVersionAsync_without_a_staged_version_throws_descriptive_NotSupportedException()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.GetLedgerApiVersionAsync();

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithLedgerApiVersion");
    }

    [Fact]
    public async Task GetUpdateByOffsetAsync_returns_the_staged_transaction_for_that_offset()
    {
        var transaction = LedgerResults.Transaction("update-1", LedgerOffset.At(5), [], [], new CommandId("cmd-1"));
        var client = FakeLedgerClient.Create().WithUpdateByOffset(5, transaction).Build();

        var result = await client.GetUpdateByOffsetAsync(5, Alice, TestContext.Current.CancellationToken);

        result.Should().BeSameAs(transaction);
    }

    [Fact]
    public async Task GetUpdateByOffsetAsync_rejects_a_non_positive_offset()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.GetUpdateByOffsetAsync(0, Alice);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetUpdateByIdAsync_returns_the_staged_transaction_for_that_id()
    {
        var transaction = LedgerResults.Transaction("update-1", LedgerOffset.At(5), [], [], new CommandId("cmd-1"));
        var client = FakeLedgerClient.Create().WithUpdateById("update-1", transaction).Build();

        var result = await client.GetUpdateByIdAsync("update-1", Alice, TestContext.Current.CancellationToken);

        result.Should().BeSameAs(transaction);
    }

    [Fact]
    public async Task GetUpdateByOffsetAsync_for_an_unstaged_offset_throws_descriptive_NotSupportedException()
    {
        var transaction = LedgerResults.Transaction("update-1", LedgerOffset.At(5), [], [], new CommandId("cmd-1"));
        var client = FakeLedgerClient.Create().WithUpdateByOffset(5, transaction).Build();

        var act = () => client.GetUpdateByOffsetAsync(6, Alice);

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithUpdateByOffset").And.Contain("offset 6");
    }

    [Fact]
    public async Task GetUpdateByIdAsync_rejects_a_blank_update_id()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.GetUpdateByIdAsync(" ", Alice);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetUpdateByIdAsync_for_an_unstaged_id_throws_descriptive_NotSupportedException()
    {
        var transaction = LedgerResults.Transaction("update-1", LedgerOffset.At(5), [], [], new CommandId("cmd-1"));
        var client = FakeLedgerClient.Create().WithUpdateById("update-1", transaction).Build();

        var act = () => client.GetUpdateByIdAsync("update-2", Alice);

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithUpdateById").And.Contain("update-2");
    }

    [Fact]
    public async Task SubmitReassignmentAsync_mints_a_command_id_when_the_submission_omits_one()
    {
        var client = FakeLedgerClient.Create().Build();
        var submission = ReassignmentSubmission.Of(
            new UnassignCommand("00cid", (SynchronizerId)"src", (SynchronizerId)"tgt"), Alice);

        var commandId = await client.SubmitReassignmentAsync(submission, TestContext.Current.CancellationToken);

        commandId.Value.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CompletionStreamAsync_honors_a_cancelled_token()
    {
        var accepted = new CompletionStreamEvent.CommandAccepted(CompletionFor("cmd-1", 7), "update-1");
        var client = FakeLedgerClient.Create().WithCompletionEvents(accepted).Build();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await CollectAsync(client.CompletionStreamAsync(Alice, cancellationToken: cts.Token));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
