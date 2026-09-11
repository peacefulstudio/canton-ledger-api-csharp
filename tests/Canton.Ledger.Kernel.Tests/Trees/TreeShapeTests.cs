// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Trees;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Trees;

public class TreeShapeTests
{
    private static readonly Identifier TemplateId = new("pkg", "Module", "Entity");

    [Fact]
    public void Assemble_nests_events_whose_node_ids_fall_inside_an_open_subtree()
    {
        var roots = TreeShape.Assemble([
            Subtree(nodeId: 0, lastDescendantNodeId: 2, "Outer"),
            Leaf(nodeId: 1, "00child"),
            Leaf(nodeId: 2, "00grandchild"),
            Leaf(nodeId: 3, "00sibling")]);

        roots.Should().HaveCount(2);
        var outer = (TreeEvent.Exercised)roots[0];
        outer.ChildEvents.Cast<TreeEvent.Created>().Select(child => child.ContractId)
            .Should().Equal("00child", "00grandchild");
        ((TreeEvent.Created)roots[1]).ContractId.Should().Be("00sibling");
    }

    [Fact]
    public void Assemble_closes_every_open_subtree_when_the_events_run_out()
    {
        var roots = TreeShape.Assemble([
            Subtree(nodeId: 0, lastDescendantNodeId: 9, "Outer"),
            Subtree(nodeId: 1, lastDescendantNodeId: 9, "Inner")]);

        var outer = (TreeEvent.Exercised)roots.Should().ContainSingle().Subject;
        var inner = (TreeEvent.Exercised)outer.ChildEvents.Should().ContainSingle().Subject;
        inner.ChoiceName.Should().Be("Inner");
    }

    [Fact]
    public void A_node_cannot_exist_without_a_way_to_decode_it()
    {
        var act = () => new TreeNode(0, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Assemble_throws_when_node_ids_do_not_strictly_ascend()
    {
        var act = () => TreeShape.Assemble([Leaf(nodeId: 3, "00first"), Leaf(nodeId: 3, "00second")]);

        act.Should().Throw<MalformedTransactionTreeException>()
            .Which.Message.Should().Be(MalformedTreeMessages.NodeIdsDoNotAscend);
    }

    [Fact]
    public void Assemble_throws_when_a_subtree_ends_before_the_exercise_that_opened_it()
    {
        var act = () => TreeShape.Assemble([Subtree(nodeId: 4, lastDescendantNodeId: 2, "Backwards")]);

        act.Should().Throw<MalformedTransactionTreeException>()
            .Which.Message.Should().Be(MalformedTreeMessages.SubtreeEndsBeforeItsExercise);
    }

    [Fact]
    public void Assemble_throws_when_a_subtree_runs_past_the_subtree_enclosing_it()
    {
        var act = () => TreeShape.Assemble([
            Subtree(nodeId: 0, lastDescendantNodeId: 2, "Outer"),
            Subtree(nodeId: 1, lastDescendantNodeId: 5, "Straddles")]);

        act.Should().Throw<MalformedTransactionTreeException>()
            .Which.Message.Should().Be(MalformedTreeMessages.SubtreeOverlapsInsteadOfNesting);
    }

    [Fact]
    public void Assemble_closes_a_finished_subtree_before_decoding_the_node_that_ended_it()
    {
        var order = new List<string>();
        TreeNode[] nodes =
        [
            new(0, () =>
            {
                order.Add("decode 0");
                return new TreeNodeContent.Subtree("Outer", 0, children =>
                {
                    order.Add("close 0");
                    return Exercised(0, "Outer", children);
                });
            }),
            new(1, () =>
            {
                order.Add("decode 1");
                return new TreeNodeContent.Leaf(Created(1, "00follower"));
            }),
        ];

        TreeShape.Assemble(nodes).Should().HaveCount(2);

        order.Should().Equal("decode 0", "close 0", "decode 1");
    }

    [Fact]
    public void EventIdOf_renders_the_node_id_invariantly()
    {
        TreeShape.EventIdOf(17).Should().Be("17");
    }

    private static TreeNode Leaf(int nodeId, string contractId) =>
        new(nodeId, () => new TreeNodeContent.Leaf(Created(nodeId, contractId)));

    private static TreeNode Subtree(int nodeId, int lastDescendantNodeId, string choice) =>
        new(nodeId, () => new TreeNodeContent.Subtree(
            choice, lastDescendantNodeId, children => Exercised(nodeId, choice, children)));

    private static TreeEvent Created(int nodeId, string contractId) =>
        new TreeEvent.Created(
            TreeShape.EventIdOf(nodeId), contractId, TemplateId, new DamlRecord(null, []), [], [], [], null, null);

    private static TreeEvent Exercised(int nodeId, string choice, EquatableArray<TreeEvent> children) =>
        new TreeEvent.Exercised(
            TreeShape.EventIdOf(nodeId),
            $"00{choice}",
            TemplateId,
            null,
            choice,
            DamlUnit.Instance,
            DamlUnit.Instance,
            true,
            [],
            [],
            children);
}
