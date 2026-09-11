// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Contracts;

namespace Canton.Ledger.Kernel.Trees;

internal static class TreeShape
{
    internal static EquatableArray<TreeEvent> Assemble(IEnumerable<TreeNode> nodes)
    {
        var roots = new List<TreeEvent>();
        var openExercises = new Stack<OpenSubtree>();
        var previousNodeId = -1;

        foreach (var node in nodes)
        {
            var nodeId = node.NodeId;
            if (nodeId <= previousNodeId)
            {
                throw NotATree(
                    $"node id {nodeId} follows node id {previousNodeId}, but node ids must strictly ascend");
            }
            previousNodeId = nodeId;

            while (openExercises.Count > 0 && nodeId > openExercises.Peek().LastDescendantNodeId)
            {
                Emit(Close(openExercises.Pop()), openExercises, roots);
            }

            var content = node.Decode();
            switch (content)
            {
                case TreeNodeContent.Subtree subtree:
                    openExercises.Push(Open(subtree, nodeId, openExercises));
                    break;
                case TreeNodeContent.Leaf leaf:
                    Emit(leaf.Event, openExercises, roots);
                    break;
                default:
                    throw new InvalidOperationException($"Unhandled tree node content: {content.GetType().Name}");
            }
        }

        while (openExercises.Count > 0)
        {
            Emit(Close(openExercises.Pop()), openExercises, roots);
        }

        return EquatableArray.Create(roots);
    }

    internal static string EventIdOf(int nodeId) => nodeId.ToString(CultureInfo.InvariantCulture);

    internal static MalformedTransactionTreeException NotATree(string detail) =>
        new($"Cannot reconstruct the transaction tree: {detail}.");

    private static OpenSubtree Open(TreeNodeContent.Subtree subtree, int nodeId, Stack<OpenSubtree> openExercises)
    {
        var lastDescendantNodeId = subtree.LastDescendantNodeId;
        if (lastDescendantNodeId < nodeId)
        {
            throw NotATree(
                $"the exercise of '{subtree.Choice}' at node id {nodeId} claims a last descendant node id of "
                + $"{lastDescendantNodeId}, which precedes the exercise itself");
        }

        if (openExercises.Count > 0 && lastDescendantNodeId > openExercises.Peek().LastDescendantNodeId)
        {
            var enclosing = openExercises.Peek();
            throw NotATree(
                $"the subtree of '{subtree.Choice}' at node id {nodeId} ends at node id {lastDescendantNodeId}, "
                + $"past the end ({enclosing.LastDescendantNodeId}) of the enclosing subtree rooted at node id "
                + $"{enclosing.NodeId}, so the two overlap instead of nesting");
        }

        return new OpenSubtree(subtree.Close, nodeId, lastDescendantNodeId);
    }

    private static void Emit(TreeEvent node, Stack<OpenSubtree> openExercises, List<TreeEvent> roots)
    {
        if (openExercises.Count > 0)
        {
            openExercises.Peek().Children.Add(node);
        }
        else
        {
            roots.Add(node);
        }
    }

    private static TreeEvent Close(OpenSubtree open) => open.Close(EquatableArray.Create(open.Children));

    private sealed class OpenSubtree(CloseSubtree close, int nodeId, int lastDescendantNodeId)
    {
        public CloseSubtree Close { get; } = close;

        public int NodeId { get; } = nodeId;

        public int LastDescendantNodeId { get; } = lastDescendantNodeId;

        public List<TreeEvent> Children { get; } = [];
    }
}
