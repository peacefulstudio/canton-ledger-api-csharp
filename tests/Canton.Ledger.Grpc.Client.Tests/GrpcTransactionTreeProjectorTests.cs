// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;
using ProtoArchivedEvent = Com.Daml.Ledger.Api.V2.ArchivedEvent;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;
using Timestamp = Google.Protobuf.WellKnownTypes.Timestamp;

namespace Canton.Ledger.Grpc.Client.Tests;

public class GrpcTransactionTreeProjectorTests
{
    [Fact]
    public void Project_throws_when_response_Transaction_is_null()
    {
        var act = () => GrpcTransactionTreeProjector.Project(new SubmitAndWaitForTransactionResponse());

        act.Should().Throw<InvalidOperationException>().WithMessage("*no Transaction was present*");
    }

    [Fact]
    public void Project_carries_the_update_id_and_offset_of_the_transaction()
    {
        var tree = GrpcTransactionTreeProjector.Project(Transaction(Created(nodeId: 0, "00aa")));

        tree.UpdateId.Should().Be("update-1");
        tree.CompletionOffset.Value.Should().Be(42L);
    }

    [Fact]
    public void Project_returns_every_event_as_a_root_when_the_transaction_has_no_exercises()
    {
        var tree = GrpcTransactionTreeProjector.Project(Transaction(
            Created(nodeId: 0, "00aa"),
            Created(nodeId: 1, "00bb"),
            Created(nodeId: 2, "00cc")));

        tree.RootEvents.Should().HaveCount(3);
        tree.RootEvents.Select(ContractIdOf).Should().Equal("00aa", "00bb", "00cc");
    }

    [Fact]
    public void Project_nests_the_events_an_exercise_caused_underneath_it()
    {
        var tree = GrpcTransactionTreeProjector.Project(Transaction(
            Exercised(nodeId: 0, lastDescendantNodeId: 2, "00target", "ExecuteSwap"),
            Created(nodeId: 1, "00aa"),
            Created(nodeId: 2, "00bb")));

        var root = tree.RootEvents.Should().ContainSingle().Subject.Should().BeOfType<TreeEvent.Exercised>().Subject;
        root.ChoiceName.Should().Be("ExecuteSwap");
        root.ChildEvents.Select(ContractIdOf).Should().Equal("00aa", "00bb");
    }

    [Fact]
    public void Project_nests_exercises_several_levels_deep()
    {
        var tree = GrpcTransactionTreeProjector.Project(Transaction(
            Exercised(nodeId: 0, lastDescendantNodeId: 4, "00root", "Outer"),
            Exercised(nodeId: 1, lastDescendantNodeId: 3, "00middle", "Inner"),
            Exercised(nodeId: 2, lastDescendantNodeId: 2, "00leafExercise", "Deepest"),
            Created(nodeId: 3, "00deepCreate"),
            Created(nodeId: 4, "00shallowCreate")));

        var outer = tree.RootEvents.Should().ContainSingle().Subject.Should().BeOfType<TreeEvent.Exercised>().Subject;
        outer.ChildEvents.Should().HaveCount(2);

        var inner = outer.ChildEvents[0].Should().BeOfType<TreeEvent.Exercised>().Subject;
        inner.ChoiceName.Should().Be("Inner");
        inner.ChildEvents.Select(ContractIdOf).Should().Equal("00leafExercise", "00deepCreate");

        var deepest = inner.ChildEvents[0].Should().BeOfType<TreeEvent.Exercised>().Subject;
        deepest.ChildEvents.Should().BeEmpty();

        ContractIdOf(outer.ChildEvents[1]).Should().Be("00shallowCreate");
    }

    [Fact]
    public void Project_keeps_sibling_subtrees_separate()
    {
        var tree = GrpcTransactionTreeProjector.Project(Transaction(
            Exercised(nodeId: 0, lastDescendantNodeId: 1, "00first", "First"),
            Created(nodeId: 1, "00fromFirst"),
            Exercised(nodeId: 2, lastDescendantNodeId: 3, "00second", "Second"),
            Created(nodeId: 3, "00fromSecond")));

        tree.RootEvents.Should().HaveCount(2);
        var first = tree.RootEvents[0].Should().BeOfType<TreeEvent.Exercised>().Subject;
        var second = tree.RootEvents[1].Should().BeOfType<TreeEvent.Exercised>().Subject;
        first.ChildEvents.Select(ContractIdOf).Should().Equal("00fromFirst");
        second.ChildEvents.Select(ContractIdOf).Should().Equal("00fromSecond");
    }

    [Fact]
    public void Project_nests_by_node_id_interval_rather_than_by_wire_order()
    {
        var tree = GrpcTransactionTreeProjector.Project(Transaction(
            Exercised(nodeId: 0, lastDescendantNodeId: 1, "00target", "Narrow"),
            Created(nodeId: 1, "00inside"),
            Created(nodeId: 2, "00outside")));

        var exercise = tree.RootEvents[0].Should().BeOfType<TreeEvent.Exercised>().Subject;
        exercise.ChildEvents.Select(ContractIdOf).Should().Equal("00inside");
        tree.RootEvents.Should().HaveCount(2);
        ContractIdOf(tree.RootEvents[1]).Should().Be("00outside");
    }

    [Fact]
    public void Project_preserves_the_consuming_flag_of_each_exercise()
    {
        var nonConsuming = Exercised(nodeId: 0, lastDescendantNodeId: 1, "00kept", "Peek");
        nonConsuming.Exercised.Consuming = false;
        var consuming = Exercised(nodeId: 1, lastDescendantNodeId: 1, "00burned", "Archive");
        consuming.Exercised.Consuming = true;

        var tree = GrpcTransactionTreeProjector.Project(Transaction(nonConsuming, consuming));

        var outer = tree.RootEvents.Should().ContainSingle().Subject.Should().BeOfType<TreeEvent.Exercised>().Subject;
        outer.Consuming.Should().BeFalse();
        outer.ChildEvents.Should().ContainSingle().Which.Should().BeOfType<TreeEvent.Exercised>()
            .Which.Consuming.Should().BeTrue();
    }

    [Fact]
    public void Project_nests_events_whose_intervening_siblings_the_participant_filtered_out()
    {
        var tree = GrpcTransactionTreeProjector.Project(Transaction(
            Exercised(nodeId: 0, lastDescendantNodeId: 10, "00target", "Wide"),
            Created(nodeId: 7, "00visible"),
            Created(nodeId: 11, "00afterTheSubtree")));

        var exercise = tree.RootEvents[0].Should().BeOfType<TreeEvent.Exercised>().Subject;
        exercise.ChildEvents.Select(ContractIdOf).Should().Equal("00visible");
        ContractIdOf(tree.RootEvents[1]).Should().Be("00afterTheSubtree");
    }

    [Fact]
    public void Project_throws_when_node_ids_do_not_strictly_ascend()
    {
        var act = () => GrpcTransactionTreeProjector.Project(Transaction(
            Created(nodeId: 3, "00late"),
            Created(nodeId: 1, "00early")));

        act.Should().Throw<MalformedTransactionTreeException>()
            .WithMessage("*node ids must strictly ascend*");
    }

    [Fact]
    public void Project_throws_when_the_same_node_id_appears_twice()
    {
        var act = () => GrpcTransactionTreeProjector.Project(Transaction(
            Created(nodeId: 2, "00first"),
            Created(nodeId: 2, "00duplicate")));

        act.Should().Throw<MalformedTransactionTreeException>()
            .WithMessage("*node ids must strictly ascend*");
    }

    [Fact]
    public void Project_throws_when_last_descendant_node_id_precedes_the_exercise()
    {
        var act = () => GrpcTransactionTreeProjector.Project(Transaction(
            Exercised(nodeId: 4, lastDescendantNodeId: 2, "00target", "Backwards")));

        act.Should().Throw<MalformedTransactionTreeException>()
            .WithMessage("*precedes the exercise itself*");
    }

    [Fact]
    public void Project_throws_when_a_subtree_runs_past_the_subtree_enclosing_it()
    {
        var act = () => GrpcTransactionTreeProjector.Project(Transaction(
            Exercised(nodeId: 0, lastDescendantNodeId: 2, "00outer", "Outer"),
            Exercised(nodeId: 1, lastDescendantNodeId: 5, "00straddles", "Straddles")));

        act.Should().Throw<MalformedTransactionTreeException>()
            .WithMessage("*overlap instead of nesting*");
    }

    [Fact]
    public void Project_throws_when_the_transaction_carries_an_archived_event()
    {
        var archived = new Event
        {
            Archived = new ProtoArchivedEvent { NodeId = 1, ContractId = "00gone", TemplateId = TemplateId },
        };

        var act = () => GrpcTransactionTreeProjector.Project(Transaction(Created(nodeId: 0, "00aa"), archived));

        act.Should().Throw<MalformedTransactionTreeException>()
            .WithMessage("*Archived event, which has no place in a transaction tree*");
    }

    [Fact]
    public void Project_populates_the_facets_the_flattened_shape_drops_from_a_created_event()
    {
        var created = new ProtoCreatedEvent
        {
            NodeId = 0,
            ContractId = "00rich",
            TemplateId = TemplateId,
            CreateArguments = new ProtoRecord(),
            ContractKey = new ProtoValue { Text = "the-key" },
            CreatedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UnixEpoch.AddSeconds(1234)),
        };
        created.WitnessParties.Add("alice");
        created.Signatories.Add("issuer");
        created.Observers.Add("bob");
        created.InterfaceViews.Add(new InterfaceView { InterfaceId = InterfaceId });

        var tree = GrpcTransactionTreeProjector.Project(Transaction(new Event { Created = created }));

        var node = tree.RootEvents.Should().ContainSingle().Subject.Should().BeOfType<TreeEvent.Created>().Subject;
        node.Signatories.Should().Equal((Party)"issuer");
        node.Observers.Should().Equal((Party)"bob");
        node.WitnessParties.Should().Equal((Party)"alice");
        node.ContractKey.Should().NotBeNull();
        node.CreatedAt.Should().Be(DateTimeOffset.UnixEpoch.AddSeconds(1234));
        node.InterfaceIds.Should().ContainSingle().Which.EntityName.Should().Be("IAsset");
    }

    [Fact]
    public void DescendantEvents_walks_the_whole_subtree_in_depth_first_pre_order()
    {
        var tree = GrpcTransactionTreeProjector.Project(Transaction(
            Exercised(nodeId: 0, lastDescendantNodeId: 4, "00root", "Outer"),
            Exercised(nodeId: 1, lastDescendantNodeId: 3, "00middle", "Inner"),
            Created(nodeId: 2, "00aa"),
            Created(nodeId: 3, "00bb"),
            Created(nodeId: 4, "00cc")));

        var descendants = tree.RootEvents[0].DescendantEvents().Select(ContractIdOf);

        descendants.Should().Equal("00middle", "00aa", "00bb", "00cc");
    }

    [Fact]
    public void ToTransactionResult_flattens_a_projected_tree_back_to_the_created_and_archived_lists()
    {
        var consuming = Exercised(nodeId: 0, lastDescendantNodeId: 2, "00burned", "Archive");
        consuming.Exercised.Consuming = true;
        var nonConsuming = Exercised(nodeId: 1, lastDescendantNodeId: 1, "00kept", "Peek");
        nonConsuming.Exercised.Consuming = false;

        var flattened = GrpcTransactionTreeProjector
            .Project(Transaction(consuming, nonConsuming, Created(nodeId: 2, "00aa")))
            .ToTransactionResult();

        flattened.UpdateId.Should().Be("update-1");
        flattened.CreatedContracts.Select(c => c.ContractId).Should().Equal("00aa");
        flattened.ArchivedContractIds.Should().Equal("00burned");
        flattened.ExercisedEvents.Select(e => e.ChoiceName).Should().BeEquivalentTo("Archive", "Peek");
    }

    private static readonly ProtoIdentifier TemplateId =
        new() { PackageId = "pkg", ModuleName = "Token.Holding", EntityName = "Holding" };

    private static readonly ProtoIdentifier InterfaceId =
        new() { PackageId = "iface-pkg", ModuleName = "Token.Api", EntityName = "IAsset" };

    private static Transaction Transaction(params Event[] events)
    {
        var transaction = new Transaction { UpdateId = "update-1", Offset = 42L, CommandId = "cmd-1" };
        transaction.Events.AddRange(events);
        return transaction;
    }

    private static Event Created(int nodeId, string contractId) => new()
    {
        Created = new ProtoCreatedEvent
        {
            NodeId = nodeId,
            ContractId = contractId,
            TemplateId = TemplateId,
            CreateArguments = new ProtoRecord(),
        },
    };

    private static Event Exercised(int nodeId, int lastDescendantNodeId, string contractId, string choice) => new()
    {
        Exercised = new ProtoExercisedEvent
        {
            NodeId = nodeId,
            LastDescendantNodeId = lastDescendantNodeId,
            ContractId = contractId,
            TemplateId = TemplateId,
            Choice = choice,
            ChoiceArgument = new ProtoValue { Unit = new Google.Protobuf.WellKnownTypes.Empty() },
        },
    };

    private static string ContractIdOf(TreeEvent evt) => evt switch
    {
        TreeEvent.Created created => created.ContractId,
        TreeEvent.Exercised exercised => exercised.ContractId,
        _ => throw new InvalidOperationException($"Unhandled tree event: {evt.GetType().Name}"),
    };
}
