// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using WireArchivedEvent = Canton.Ledger.Rest.Client.Raw.ArchivedEvent;
using WireCreatedEvent = Canton.Ledger.Rest.Client.Raw.CreatedEvent;
using WireEvent = Canton.Ledger.Rest.Client.Raw.Event;
using WireExercisedEvent = Canton.Ledger.Rest.Client.Raw.ExercisedEvent;
using WireIdentifier = Canton.Ledger.Rest.Client.Raw.Identifier;
using WireInterfaceView = Canton.Ledger.Rest.Client.Raw.InterfaceView;
using WireRecord = Canton.Ledger.Rest.Client.Raw.Record;
using WireTransaction = Canton.Ledger.Rest.Client.Raw.Transaction;
using WireValue = Canton.Ledger.Rest.Client.Raw.Value;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestTransactionTreeProjectorTests
{
    [Fact]
    public void Project_throws_when_the_transaction_is_null()
    {
        var act = () => RestTransactionTreeProjector.Project(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Project_carries_the_update_id_and_offset_of_the_transaction()
    {
        var tree = RestTransactionTreeProjector.Project(Transaction(Created(nodeId: 0, "00aa")));

        tree.UpdateId.Should().Be("update-1");
        tree.CompletionOffset.Value.Should().Be(42L);
    }

    [Fact]
    public void Project_returns_every_event_as_a_root_when_the_transaction_has_no_exercises()
    {
        var tree = RestTransactionTreeProjector.Project(Transaction(
            Created(nodeId: 0, "00aa"),
            Created(nodeId: 1, "00bb"),
            Created(nodeId: 2, "00cc")));

        tree.RootEvents.Should().HaveCount(3);
        tree.RootEvents.Select(ContractIdOf).Should().Equal("00aa", "00bb", "00cc");
    }

    [Fact]
    public void Project_nests_the_events_an_exercise_caused_underneath_it()
    {
        var tree = RestTransactionTreeProjector.Project(Transaction(
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
        var tree = RestTransactionTreeProjector.Project(Transaction(
            Exercised(nodeId: 0, lastDescendantNodeId: 4, "00root", "Outer"),
            Exercised(nodeId: 1, lastDescendantNodeId: 3, "00middle", "Inner"),
            Exercised(nodeId: 2, lastDescendantNodeId: 2, "00leafExercise", "Deepest"),
            Created(nodeId: 3, "00deepCreate"),
            Created(nodeId: 4, "00shallowCreate")));

        var outer = tree.RootEvents.Should().ContainSingle().Subject.Should().BeOfType<TreeEvent.Exercised>().Subject;
        outer.ChildEvents.Select(ContractIdOf).Should().Equal("00middle", "00shallowCreate");
        var inner = outer.ChildEvents[0].Should().BeOfType<TreeEvent.Exercised>().Subject;
        inner.ChildEvents.Select(ContractIdOf).Should().Equal("00leafExercise", "00deepCreate");
    }

    [Fact]
    public void Project_keeps_sibling_subtrees_separate()
    {
        var tree = RestTransactionTreeProjector.Project(Transaction(
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
        var tree = RestTransactionTreeProjector.Project(Transaction(
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
        nonConsuming.ExercisedEvent!.Consuming = false;
        var consuming = Exercised(nodeId: 1, lastDescendantNodeId: 1, "00burned", "Archive");
        consuming.ExercisedEvent!.Consuming = true;

        var tree = RestTransactionTreeProjector.Project(Transaction(nonConsuming, consuming));

        var outer = tree.RootEvents.Should().ContainSingle().Subject.Should().BeOfType<TreeEvent.Exercised>().Subject;
        outer.Consuming.Should().BeFalse();
        outer.ChildEvents.Should().ContainSingle().Which.Should().BeOfType<TreeEvent.Exercised>()
            .Which.Consuming.Should().BeTrue();
    }

    [Fact]
    public void Project_treats_an_absent_consuming_flag_as_non_consuming()
    {
        var tree = RestTransactionTreeProjector.Project(Transaction(
            Exercised(nodeId: 0, lastDescendantNodeId: 0, "00kept", "Peek")));

        tree.RootEvents.Should().ContainSingle().Subject.Should().BeOfType<TreeEvent.Exercised>()
            .Which.Consuming.Should().BeFalse();
    }

    [Fact]
    public void Project_nests_events_whose_intervening_siblings_the_participant_filtered_out()
    {
        var tree = RestTransactionTreeProjector.Project(Transaction(
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
        var act = () => RestTransactionTreeProjector.Project(Transaction(
            Created(nodeId: 3, "00late"),
            Created(nodeId: 1, "00early")));

        act.Should().Throw<MalformedTransactionTreeException>()
            .WithMessage("*node ids must strictly ascend*");
    }

    [Fact]
    public void Project_throws_when_the_same_node_id_appears_twice()
    {
        var act = () => RestTransactionTreeProjector.Project(Transaction(
            Created(nodeId: 2, "00aa"),
            Created(nodeId: 2, "00bb")));

        act.Should().Throw<MalformedTransactionTreeException>()
            .WithMessage("*node ids must strictly ascend*");
    }

    [Fact]
    public void Project_throws_when_last_descendant_node_id_precedes_the_exercise()
    {
        var act = () => RestTransactionTreeProjector.Project(Transaction(
            Exercised(nodeId: 5, lastDescendantNodeId: 4, "00target", "ExecuteSwap")));

        act.Should().Throw<MalformedTransactionTreeException>()
            .WithMessage("*precedes the exercise itself*");
    }

    [Fact]
    public void Project_throws_when_a_subtree_runs_past_the_subtree_enclosing_it()
    {
        var act = () => RestTransactionTreeProjector.Project(Transaction(
            Exercised(nodeId: 0, lastDescendantNodeId: 2, "00outer", "Outer"),
            Exercised(nodeId: 1, lastDescendantNodeId: 7, "00inner", "Inner")));

        act.Should().Throw<MalformedTransactionTreeException>()
            .WithMessage("*overlap instead of nesting*");
    }

    [Fact]
    public void Project_throws_when_an_exercise_states_no_last_descendant_node_id()
    {
        var exercised = Exercised(nodeId: 0, lastDescendantNodeId: 0, "00target", "ExecuteSwap");
        exercised.ExercisedEvent!.LastDescendantNodeId = null;

        var act = () => RestTransactionTreeProjector.Project(Transaction(exercised));

        act.Should().Throw<MalformedTransactionTreeException>()
            .WithMessage("*states no last descendant node id*");
    }

    [Fact]
    public void Project_throws_when_an_event_carries_no_node_id()
    {
        var created = Created(nodeId: 0, "00aa");
        created.CreatedEvent!.NodeId = null;

        var act = () => RestTransactionTreeProjector.Project(Transaction(created));

        act.Should().Throw<MalformedTransactionTreeException>()
            .WithMessage("*carries no node id*");
    }

    [Fact]
    public void Project_throws_when_the_transaction_carries_an_archived_event()
    {
        var archived = new WireEvent
        {
            ArchivedEvent = new WireArchivedEvent { NodeId = 1, ContractId = "00gone", TemplateId = TemplateId },
        };

        var act = () => RestTransactionTreeProjector.Project(Transaction(Created(nodeId: 0, "00aa"), archived));

        act.Should().Throw<MalformedTransactionTreeException>()
            .WithMessage("*ArchivedEvent, which has no place in a transaction tree*");
    }

    [Fact]
    public void Project_throws_when_a_created_event_has_no_template_id()
    {
        var created = Created(nodeId: 0, "00aa");
        created.CreatedEvent!.TemplateId = null!;

        var act = () => RestTransactionTreeProjector.Project(Transaction(created));

        act.Should().Throw<InvalidOperationException>().WithMessage("*has no templateId*");
    }

    [Fact]
    public void Project_populates_the_facets_the_flattened_shape_drops_from_a_created_event()
    {
        var created = new WireEvent
        {
            CreatedEvent = new WireCreatedEvent
            {
                NodeId = 0,
                ContractId = "00rich",
                TemplateId = TemplateId,
                CreateArgument = new WireRecord(),
                ContractKey = new WireValue { Text = "the-key" },
                CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(1234),
                WitnessParties = ["alice"],
                Signatories = ["issuer"],
                Observers = ["bob"],
                InterfaceViews = [new WireInterfaceView { InterfaceId = InterfaceId }],
            },
        };

        var tree = RestTransactionTreeProjector.Project(Transaction(created));

        var node = tree.RootEvents.Should().ContainSingle().Subject.Should().BeOfType<TreeEvent.Created>().Subject;
        node.Signatories.Should().Equal((Party)"issuer");
        node.Observers.Should().Equal((Party)"bob");
        node.WitnessParties.Should().Equal((Party)"alice");
        node.ContractKey.Should().NotBeNull();
        node.CreatedAt.Should().Be(DateTimeOffset.UnixEpoch.AddSeconds(1234));
        node.InterfaceIds.Should().ContainSingle().Which.EntityName.Should().Be("IAsset");
    }

    [Fact]
    public void Project_populates_the_facets_the_flattened_shape_drops_from_an_exercised_event()
    {
        var exercised = new WireEvent
        {
            ExercisedEvent = new WireExercisedEvent
            {
                NodeId = 0,
                LastDescendantNodeId = 0,
                ContractId = "00rich",
                TemplateId = TemplateId,
                InterfaceId = InterfaceId,
                Choice = "ExecuteSwap",
                Consuming = true,
                ActingParties = ["alice"],
                WitnessParties = ["bob", "carol"],
            },
        };

        var tree = RestTransactionTreeProjector.Project(Transaction(exercised));

        var node = tree.RootEvents.Should().ContainSingle().Subject.Should().BeOfType<TreeEvent.Exercised>().Subject;
        node.EventId.Should().Be("0");
        node.ChoiceName.Should().Be("ExecuteSwap");
        node.Consuming.Should().BeTrue();
        node.ActingParties.Should().Equal((Party)"alice");
        node.WitnessParties.Should().Equal((Party)"bob", (Party)"carol");
        node.InterfaceId.Should().Be(new RuntimeIdentifier("iface-pkg", "Token.Api", "IAsset"));
    }

    [Fact]
    public void Project_throws_when_a_created_event_has_no_create_argument()
    {
        var created = Created(nodeId: 0, "00aa");
        created.CreatedEvent!.CreateArgument = null!;

        var act = () => RestTransactionTreeProjector.Project(Transaction(created));

        act.Should().Throw<InvalidOperationException>().WithMessage("*has no createArgument*");
    }

    [Fact]
    public void DescendantEvents_walks_the_whole_subtree_in_depth_first_pre_order()
    {
        var tree = RestTransactionTreeProjector.Project(Transaction(
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
        consuming.ExercisedEvent!.Consuming = true;
        var nonConsuming = Exercised(nodeId: 1, lastDescendantNodeId: 1, "00kept", "Peek");
        nonConsuming.ExercisedEvent!.Consuming = false;

        var flattened = RestTransactionTreeProjector
            .Project(Transaction(consuming, nonConsuming, Created(nodeId: 2, "00aa")))
            .ToTransactionResult();

        flattened.UpdateId.Should().Be("update-1");
        flattened.CreatedContracts.Select(c => c.ContractId).Should().Equal("00aa");
        flattened.ArchivedContractIds.Should().Equal("00burned");
        flattened.ExercisedEvents.Select(e => e.ChoiceName).Should().BeEquivalentTo("Archive", "Peek");
    }

    private static readonly WireIdentifier TemplateId =
        new() { PackageId = "pkg", ModuleName = "Token.Holding", EntityName = "Holding" };

    private static readonly WireIdentifier InterfaceId =
        new() { PackageId = "iface-pkg", ModuleName = "Token.Api", EntityName = "IAsset" };

    private static WireTransaction Transaction(params WireEvent[] events) =>
        new() { UpdateId = "update-1", Offset = "42", CommandId = "cmd-1", Events = events };

    private static WireEvent Created(int nodeId, string contractId) => new()
    {
        CreatedEvent = new WireCreatedEvent
        {
            NodeId = nodeId,
            ContractId = contractId,
            TemplateId = TemplateId,
            CreateArgument = new WireRecord(),
        },
    };

    private static WireEvent Exercised(int nodeId, int lastDescendantNodeId, string contractId, string choice) => new()
    {
        ExercisedEvent = new WireExercisedEvent
        {
            NodeId = nodeId,
            LastDescendantNodeId = lastDescendantNodeId,
            ContractId = contractId,
            TemplateId = TemplateId,
            Choice = choice,
        },
    };

    private static string ContractIdOf(TreeEvent evt) => evt switch
    {
        TreeEvent.Created created => created.ContractId,
        TreeEvent.Exercised exercised => exercised.ContractId,
        _ => throw new InvalidOperationException($"Unhandled tree event: {evt.GetType().Name}"),
    };
}
