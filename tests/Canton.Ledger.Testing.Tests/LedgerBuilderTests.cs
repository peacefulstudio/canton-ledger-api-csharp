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

public class LedgerBuilderTests
{
    private static readonly Party Bob = new("bob");
    private static readonly ContractId<DemoAsset> Cid = new("cid1");
    private static readonly DamlRecord Payload = new DemoAsset(Bob, Bob, "GOLD", 42m).ToRecord();

    [Fact]
    public void LedgerEvents_Created_wraps_the_AcsSnapshotEntry_Created_variant()
    {
        var entry = LedgerEvents.Created(Cid, Payload, LedgerOffset.At(1), (SynchronizerId)"sync1", new[] { Bob });

        var created = entry.Should().BeOfType<AcsSnapshotEntry<DemoAsset>.Created>().Subject;
        created.ContractId.Should().Be(Cid);
        created.Payload.Should().BeSameAs(Payload);
        created.Offset.Should().Be(LedgerOffset.At(1));
        created.WitnessParties.Should().ContainSingle().Which.Should().Be(Bob);
    }

    [Fact]
    public void LedgerEvents_Checkpoint_wraps_the_AcsSnapshotEntry_Checkpoint_variant()
    {
        var entry = LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(9));

        entry.Should().BeOfType<AcsSnapshotEntry<DemoAsset>.Checkpoint>().Which.Resume.Offset.Should().Be(LedgerOffset.At(9));
    }

    [Fact]
    public void LedgerEvents_StreamError_wraps_the_AcsSnapshotEntry_StreamError_variant()
    {
        var entry = LedgerEvents.StreamError<DemoAsset>(14, "boom");

        var error = entry.Should().BeOfType<AcsSnapshotEntry<DemoAsset>.StreamError>().Subject;
        error.StatusCode.Should().Be(14);
        error.Message.Should().Be("boom");
    }

    [Fact]
    public void LedgerEvents_Unclassified_wraps_the_AcsSnapshotEntry_Unclassified_variant()
    {
        var entry = LedgerEvents.Unclassified<DemoAsset>(LedgerOffset.At(7), "unmapped-template");

        var unclassified = entry.Should().BeOfType<AcsSnapshotEntry<DemoAsset>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(7));
        unclassified.Kind.Should().Be("unmapped-template");
    }

    [Fact]
    public void ContractEvents_wraps_each_stream_variant()
    {
        var witnesses = new[] { Bob };
        ContractEvents.Created(Cid, Payload, LedgerOffset.At(1), (SynchronizerId)"s", witnesses)
            .Should().BeOfType<ContractStreamEvent<DemoAsset>.Created>();
        ContractEvents.Archived<DemoAsset>(Cid, LedgerOffset.At(2), (SynchronizerId)"s", witnesses)
            .Should().BeOfType<ContractStreamEvent<DemoAsset>.Archived>();
        ContractEvents.Exercised<DemoAsset>(Cid, "Transfer", new DamlText("arg"), new DamlText("res"), consuming: true, LedgerOffset.At(3), (SynchronizerId)"s", witnesses)
            .Should().BeOfType<ContractStreamEvent<DemoAsset>.Exercised>();
        ContractEvents.Assigned(Cid, Payload, LedgerOffset.At(4), (SynchronizerId)"src", (SynchronizerId)"tgt", "rid", 1L, witnesses)
            .Should().BeOfType<ContractStreamEvent<DemoAsset>.Assigned>();
        ContractEvents.Unassigned<DemoAsset>(Cid, LedgerOffset.At(5), (SynchronizerId)"src", (SynchronizerId)"tgt", "rid", 1L, witnesses)
            .Should().BeOfType<ContractStreamEvent<DemoAsset>.Unassigned>();
        ContractEvents.Checkpoint<DemoAsset>(LedgerOffset.At(6))
            .Should().BeOfType<ContractStreamEvent<DemoAsset>.Checkpoint>();
        ContractEvents.StreamError<DemoAsset>(14, "boom")
            .Should().BeOfType<ContractStreamEvent<DemoAsset>.StreamError>();
        ContractEvents.Unclassified<DemoAsset>(LedgerOffset.At(7), UnclassifiedKind.MissingSynchronizerId)
            .Should().BeOfType<ContractStreamEvent<DemoAsset>.Unclassified>();
    }

    [Fact]
    public void LedgerOutcomes_wraps_each_exercise_outcome_variant()
    {
        LedgerOutcomes.One("x").Should().BeOfType<ExerciseOutcome<string>.One>().Which.Result.Should().Be("x");
        LedgerOutcomes.None<string>().Should().BeOfType<ExerciseOutcome<string>.None>();
        LedgerOutcomes.Many<string>(2, new[] { "a", "b" }).Should().BeOfType<ExerciseOutcome<string>.Many>().Which.Count.Should().Be(2);
        LedgerOutcomes.DamlError<string>(DamlErrorCategory.ContentionOnSharedResources, "E1", "conflict", new Dictionary<string, string>())
            .Should().BeOfType<ExerciseOutcome<string>.DamlError>().Which.ErrorId.Should().Be("E1");
        LedgerOutcomes.InfraError<string>(14, "unavailable").Should().BeOfType<ExerciseOutcome<string>.InfraError>().Which.StatusCode.Should().Be(14);
    }

    [Fact]
    public void LedgerResults_Transaction_wraps_a_TransactionResult()
    {
        var result = LedgerResults.Transaction(
            "update1",
            LedgerOffset.At(5),
            new[] { new CreatedContract("cid1", DemoAsset.TemplateId, "{}") },
            new[] { "archived1" },
            (CommandId)"cmd1");

        result.UpdateId.Should().Be("update1");
        result.CompletionOffset.Should().Be(LedgerOffset.At(5));
        result.CreatedContracts.Should().ContainSingle();
        result.ArchivedContractIds.Should().ContainSingle();
        result.CommandId.Should().Be((CommandId)"cmd1");
    }

    [Fact]
    public void LedgerResults_SubmitAndWait_wraps_a_SubmitAndWaitResult()
    {
        var result = LedgerResults.SubmitAndWait((CommandId)"cmd1", "update1", LedgerOffset.At(5));

        result.CommandId.Should().Be((CommandId)"cmd1");
        result.UpdateId.Should().Be("update1");
        result.CompletionOffset.Should().Be(LedgerOffset.At(5));
    }

    [Fact]
    public void LedgerResults_TypedContract_wraps_a_typed_Contract()
    {
        var asset = new DemoAsset(Bob, Bob, "GOLD", 42m);

        var contract = LedgerResults.TypedContract(Cid, asset);

        contract.Id.Should().Be(Cid);
        contract.Data.Should().Be(asset);
    }
}
