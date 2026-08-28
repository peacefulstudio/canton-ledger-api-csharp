// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class FakeLedgerClientTests
{
    private static readonly Party Alice = new("alice");
    private static readonly SynchronizerId Sync = (SynchronizerId)"sync1";
    private static readonly DamlRecord Payload = new DemoAsset(Alice, Alice, "GOLD", 1m).ToRecord();

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
    public async Task SubscribeActiveAsync_replays_staged_snapshot_entries_in_order()
    {
        var created = LedgerEvents.Created(
            new ContractId<DemoAsset>("cid1"),
            new DemoAsset(Alice, Alice, "GOLD", 1m).ToRecord(),
            LedgerOffset.At(1),
            (SynchronizerId)"sync1",
            new[] { Alice });
        var checkpoint = LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(2));
        var client = FakeLedgerClient.Create().WithActiveContracts(created, checkpoint).Build();

        var entries = await CollectAsync(client.SubscribeActiveAsync<DemoAsset>(Alice, cancellationToken: TestContext.Current.CancellationToken));

        entries.Should().Equal(created, checkpoint);
    }

    [Fact]
    public void SubscribeActiveAsync_for_unstaged_type_throws_descriptive_NotSupportedException()
    {
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.Begin))
            .Build();

        var act = () => client.SubscribeActiveAsync<OtherAsset>(Alice);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*WithActiveContracts*")
            .WithMessage("*OtherAsset*");
    }

    [Fact]
    public async Task SubscribeAsync_replays_staged_contract_stream_events()
    {
        var created = ContractEvents.Created(
            new ContractId<DemoAsset>("cid1"),
            new DemoAsset(Alice, Alice, "GOLD", 1m).ToRecord(),
            LedgerOffset.At(1),
            (SynchronizerId)"sync1",
            new[] { Alice });
        var client = FakeLedgerClient.Create().WithContractEvents(created).Build();

        var entries = await CollectAsync(client.SubscribeAsync<DemoAsset>(Alice, cancellationToken: TestContext.Current.CancellationToken));

        entries.Should().ContainSingle().Which.Should().Be(created);
    }

    [Fact]
    public async Task SubscribeLedgerEffectsAsync_replays_staged_contract_stream_events()
    {
        var archived = ContractEvents.Archived<DemoAsset>(
            new ContractId<DemoAsset>("cid1"),
            LedgerOffset.At(3),
            (SynchronizerId)"sync1",
            new[] { Alice });
        var client = FakeLedgerClient.Create().WithLedgerEffects(archived).Build();

        var entries = await CollectAsync(client.SubscribeLedgerEffectsAsync<DemoAsset>(Alice, cancellationToken: TestContext.Current.CancellationToken));

        entries.Should().ContainSingle().Which.Should().Be(archived);
    }

    [Fact]
    public async Task TryExerciseAsync_returns_the_staged_outcome()
    {
        var outcome = LedgerOutcomes.One("choice-result");
        var client = FakeLedgerClient.Create().WithExerciseResult(outcome).Build();

        var result = await client.TryExerciseAsync<string>(
            command: null!,
            submitter: Alice,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeSameAs(outcome);
    }

    [Fact]
    public async Task TryExerciseAsync_for_unstaged_type_throws_descriptive_NotSupportedException()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.TryExerciseAsync<string>(command: null!, submitter: Alice);

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithExerciseResult").And.Contain("String");
    }

    [Fact]
    public async Task TryCreateAsync_returns_the_staged_outcome()
    {
        var outcome = LedgerOutcomes.One(new ContractId<DemoAsset>("cid1"));
        var client = FakeLedgerClient.Create().WithCreateResult(outcome).Build();

        var result = await client.TryCreateAsync(
            new DemoAsset(Alice, Alice, "GOLD", 1m),
            Alice,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeSameAs(outcome);
    }

    public static TheoryData<ExerciseOutcome<TransactionResult>> SubmissionOutcomes => new()
    {
        LedgerOutcomes.One(LedgerResults.Transaction(
            "update-1",
            LedgerOffset.At(5),
            new[] { new CreatedContract("cid1", DemoAsset.TemplateId, "{}") },
            new[] { "archived1" },
            (CommandId)"cmd-1")),
        LedgerOutcomes.DamlError<TransactionResult>(
            DamlErrorCategory.InvalidGivenCurrentSystemStateOther,
            "UNHANDLED_EXCEPTION",
            "assertion failed",
            new Dictionary<string, string>()),
        LedgerOutcomes.InfraError<TransactionResult>(14, "participant unavailable"),
    };

    [Theory]
    [MemberData(nameof(SubmissionOutcomes))]
    public async Task TrySubmitAndWaitForTransactionAsync_returns_the_staged_outcome(
        ExerciseOutcome<TransactionResult> outcome)
    {
        var client = FakeLedgerClient.Create().WithSubmissionOutcome(outcome).Build();

        var result = await client.TrySubmitAndWaitForTransactionAsync(
            DemoSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeSameAs(outcome);
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_with_explicit_submitter_returns_the_staged_outcome()
    {
        var outcome = LedgerOutcomes.One(LedgerResults.Transaction(
            "update-1", LedgerOffset.At(5), Array.Empty<CreatedContract>(), Array.Empty<string>(), (CommandId)"cmd-1"));
        var client = FakeLedgerClient.Create().WithSubmissionOutcome(outcome).Build();

        var result = await client.TrySubmitAndWaitForTransactionAsync(
            DemoSubmission(), Alice, cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeSameAs(outcome);
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_when_unstaged_throws_descriptive_NotSupportedException()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.TrySubmitAndWaitForTransactionAsync(DemoSubmission());

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithSubmissionOutcome")
            .And.Contain(nameof(FakeLedgerClient.TrySubmitAndWaitForTransactionAsync));
    }

    private static CommandsSubmission DemoSubmission() =>
        CommandsSubmission.Single(
            ExerciseCommand.For(new ContractId<DemoAsset>("cid1"), (ChoiceName)"Archive", DamlRecord.Create()),
            Alice);

    [Fact]
    public async Task GetLedgerEndAsync_returns_the_staged_offset()
    {
        var client = FakeLedgerClient.Create().WithLedgerEnd(LedgerOffset.At(42)).Build();

        var end = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        end.Should().Be(LedgerOffset.At(42));
    }

    [Fact]
    public async Task GetLedgerEndAsync_advances_by_one_offset_across_a_committed_create()
    {
        var client = FakeLedgerClient.Create()
            .WithLedgerEnd(LedgerOffset.At(42))
            .WithCreateResult(LedgerOutcomes.One(new ContractId<DemoAsset>("cid1")))
            .Build();

        var before = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        await client.TryCreateAsync(
            new DemoAsset(Alice, Alice, "GOLD", 1m), Alice, cancellationToken: TestContext.Current.CancellationToken);
        var after = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        before.Should().Be(LedgerOffset.At(42));
        after.Should().Be(LedgerOffset.At(43));
    }

    [Fact]
    public async Task GetLedgerEndAsync_advances_by_one_offset_across_a_committed_exercise()
    {
        var client = FakeLedgerClient.Create()
            .WithLedgerEnd(LedgerOffset.At(42))
            .WithExerciseResult(LedgerOutcomes.One("choice-result"))
            .Build();

        await client.TryExerciseAsync<string>(
            command: null!, submitter: Alice, cancellationToken: TestContext.Current.CancellationToken);

        var end = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        end.Should().Be(LedgerOffset.At(43));
    }

    [Fact]
    public async Task GetLedgerEndAsync_advances_by_one_offset_across_a_committed_submission()
    {
        var client = FakeLedgerClient.Create()
            .WithLedgerEnd(LedgerOffset.At(42))
            .WithSubmissionOutcome(LedgerOutcomes.One(LedgerResults.Transaction(
                "update-1", LedgerOffset.At(7), Array.Empty<CreatedContract>(), Array.Empty<string>(), (CommandId)"cmd-1")))
            .Build();

        await client.TrySubmitAndWaitForTransactionAsync(
            DemoSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        var end = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        end.Should().Be(LedgerOffset.At(43));
    }

    [Fact]
    public async Task GetLedgerEndAsync_advances_once_per_committed_write()
    {
        var client = FakeLedgerClient.Create()
            .WithLedgerEnd(LedgerOffset.At(42))
            .WithCreateResult(LedgerOutcomes.One(new ContractId<DemoAsset>("cid1")))
            .Build();

        for (var write = 0; write < 3; write++)
        {
            await client.TryCreateAsync(
                new DemoAsset(Alice, Alice, "GOLD", 1m), Alice, cancellationToken: TestContext.Current.CancellationToken);
        }

        var end = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        end.Should().Be(LedgerOffset.At(45));
    }

    public static TheoryData<ExerciseOutcome<TransactionResult>> CommittedSubmissionOutcomes => new()
    {
        LedgerOutcomes.One(LedgerResults.Transaction(
            "update-1", LedgerOffset.At(7), Array.Empty<CreatedContract>(), Array.Empty<string>(), (CommandId)"cmd-1")),
        LedgerOutcomes.None<TransactionResult>(),
        LedgerOutcomes.Many<TransactionResult>(2, new[] { "cid1", "cid2" }),
    };

    [Theory]
    [MemberData(nameof(CommittedSubmissionOutcomes))]
    public async Task GetLedgerEndAsync_advances_across_every_outcome_that_committed(
        ExerciseOutcome<TransactionResult> outcome)
    {
        var client = FakeLedgerClient.Create()
            .WithLedgerEnd(LedgerOffset.At(42))
            .WithSubmissionOutcome(outcome)
            .Build();

        await client.TrySubmitAndWaitForTransactionAsync(
            DemoSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        var end = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        end.Should().Be(LedgerOffset.At(43));
    }

    [Fact]
    public async Task GetLedgerEndAsync_does_not_advance_when_the_write_throws_for_want_of_staging()
    {
        var client = FakeLedgerClient.Create().WithLedgerEnd(LedgerOffset.At(42)).Build();

        var act = () => client.TryCreateAsync(new DemoAsset(Alice, Alice, "GOLD", 1m), Alice);

        await act.Should().ThrowAsync<NotSupportedException>();
        var end = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        end.Should().Be(LedgerOffset.At(42));
    }

    [Fact]
    public async Task GetLedgerEndAsync_counts_every_write_when_writes_run_concurrently()
    {
        const int concurrentWrites = 64;
        var client = FakeLedgerClient.Create()
            .WithLedgerEnd(LedgerOffset.At(42))
            .WithCreateResult(LedgerOutcomes.One(new ContractId<DemoAsset>("cid1")))
            .Build();

        await Task.WhenAll(Enumerable.Range(0, concurrentWrites).Select(_ => Task.Run(
            () => client.TryCreateAsync(
                new DemoAsset(Alice, Alice, "GOLD", 1m), Alice, cancellationToken: TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken)));

        var end = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        end.Should().Be(LedgerOffset.At(42 + concurrentWrites));
    }

    public static TheoryData<ExerciseOutcome<TransactionResult>> UncommittedSubmissionOutcomes => new()
    {
        LedgerOutcomes.DamlError<TransactionResult>(
            DamlErrorCategory.InvalidGivenCurrentSystemStateOther,
            "UNHANDLED_EXCEPTION",
            "assertion failed",
            new Dictionary<string, string>()),
        LedgerOutcomes.InfraError<TransactionResult>(14, "participant unavailable"),
    };

    [Theory]
    [MemberData(nameof(UncommittedSubmissionOutcomes))]
    public async Task GetLedgerEndAsync_does_not_advance_across_a_write_that_did_not_commit(
        ExerciseOutcome<TransactionResult> outcome)
    {
        var client = FakeLedgerClient.Create()
            .WithLedgerEnd(LedgerOffset.At(42))
            .WithSubmissionOutcome(outcome)
            .Build();

        await client.TrySubmitAndWaitForTransactionAsync(
            DemoSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        var end = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        end.Should().Be(LedgerOffset.At(42));
    }

    [Fact]
    public async Task GetLedgerEndAsync_still_throws_after_a_committed_write_when_no_end_was_staged()
    {
        var client = FakeLedgerClient.Create()
            .WithCreateResult(LedgerOutcomes.One(new ContractId<DemoAsset>("cid1")))
            .Build();

        await client.TryCreateAsync(
            new DemoAsset(Alice, Alice, "GOLD", 1m), Alice, cancellationToken: TestContext.Current.CancellationToken);

        var act = () => client.GetLedgerEndAsync();

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain(nameof(client.GetLedgerEndAsync));
    }

    [Fact]
    public async Task Unconfigured_members_throw_a_descriptive_NotSupportedException_naming_the_member()
    {
        var client = FakeLedgerClient.Create().Build();

        var ledgerEnd = () => client.GetLedgerEndAsync();
        var submitAndWait = () => client.SubmitAndWaitAsync(null!);
        var forTransaction = () => client.TrySubmitAndWaitForTransactionAsync(null!);

        (await ledgerEnd.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain(nameof(client.GetLedgerEndAsync));
        (await submitAndWait.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain(nameof(client.SubmitAndWaitAsync));
        (await forTransaction.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain(nameof(client.TrySubmitAndWaitForTransactionAsync));
    }

    [Fact]
    public void Staging_one_type_does_not_configure_another_type()
    {
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.Begin))
            .Build();

        var demo = () => client.SubscribeActiveAsync<DemoAsset>(Alice);
        var other = () => client.SubscribeActiveAsync<OtherAsset>(Alice);

        demo.Should().NotThrow();
        other.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task Build_snapshots_staged_events_so_later_builder_mutation_is_ignored()
    {
        var builder = FakeLedgerClient.Create()
            .WithActiveContracts(LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(1)));
        var client = builder.Build();
        builder.WithActiveContracts(LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(99)));

        var entries = await CollectAsync(client.SubscribeActiveAsync<DemoAsset>(Alice, cancellationToken: TestContext.Current.CancellationToken));

        entries.Should().ContainSingle();
        entries[0].Should().BeOfType<AcsSnapshotEntry<DemoAsset>.Checkpoint>()
            .Which.Resume.Offset.Should().Be(LedgerOffset.At(1));
    }

    [Fact]
    public async Task SubscribeAsync_honours_the_exclusive_fromOffset_and_inclusive_toOffset()
    {
        var client = FakeLedgerClient.Create()
            .WithContractEvents(
                ContractEvents.Created(new ContractId<DemoAsset>("cid1"), Payload, LedgerOffset.At(1), Sync, [Alice]),
                ContractEvents.Created(new ContractId<DemoAsset>("cid2"), Payload, LedgerOffset.At(2), Sync, [Alice]),
                ContractEvents.Created(new ContractId<DemoAsset>("cid3"), Payload, LedgerOffset.At(3), Sync, [Alice]))
            .Build();

        var events = await CollectAsync(client.SubscribeAsync<DemoAsset>(
            Alice, LedgerOffset.At(1), LedgerOffset.At(2), TestContext.Current.CancellationToken));

        events.OfType<ContractStreamEvent<DemoAsset>.Created>().Select(e => e.ContractId.Value)
            .Should().Equal("cid2");
    }

    [Fact]
    public async Task SubscribeActiveAsync_honours_activeAtOffset_while_still_replaying_the_terminal_Checkpoint()
    {
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(
                LedgerEvents.Created(new ContractId<DemoAsset>("cid1"), Payload, LedgerOffset.At(4), Sync, [Alice]),
                LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(9)))
            .Build();

        var entries = await CollectAsync(client.SubscribeActiveAsync<DemoAsset>(
            Alice, LedgerOffset.At(3), TestContext.Current.CancellationToken));

        entries.Should().ContainSingle().Which.Should().BeOfType<AcsSnapshotEntry<DemoAsset>.Checkpoint>();
    }

    [Fact]
    public async Task SubscribeAsync_with_pre_cancelled_token_throws_OperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var client = FakeLedgerClient.Create()
            .WithContractEvents(
                ContractEvents.Created(new ContractId<DemoAsset>("cid1"), Payload, LedgerOffset.At(1), Sync, [Alice]))
            .Build();

        var act = async () => await CollectAsync(client.SubscribeAsync<DemoAsset>(
            Alice, LedgerOffset.At(50), LedgerOffset.At(60), cts.Token));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Dispose_and_DisposeAsync_are_no_ops()
    {
        var client = FakeLedgerClient.Create().Build();

        var dispose = () => client.Dispose();
        var disposeAsync = async () => await client.DisposeAsync();

        dispose.Should().NotThrow();
        await disposeAsync.Should().NotThrowAsync();
    }
}
