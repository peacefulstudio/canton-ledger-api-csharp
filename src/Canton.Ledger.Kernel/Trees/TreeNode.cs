// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;

namespace Canton.Ledger.Kernel.Trees;

internal delegate TreeEvent CloseSubtree(IReadOnlyList<TreeEvent> children);

internal sealed record TreeNode(int NodeId, Func<TreeNodeContent> Decode)
{
    internal Func<TreeNodeContent> Decode { get; } =
        Decode ?? throw new ArgumentNullException(nameof(Decode));
}

internal abstract record TreeNodeContent
{
    private TreeNodeContent()
    {
    }

    internal sealed record Leaf(TreeEvent Event) : TreeNodeContent;

    internal sealed record Subtree(string Choice, int LastDescendantNodeId, CloseSubtree Close) : TreeNodeContent;
}
