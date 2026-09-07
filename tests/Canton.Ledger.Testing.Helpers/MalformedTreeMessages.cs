// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// The exact <c>MalformedTransactionTreeException</c> messages both transports must produce for the
/// same malformed node-id shape. Each transport's tree-projector suite asserts against these, so an
/// edit to the shared guard that changes one transport's wording fails on both.
/// </summary>
public static class MalformedTreeMessages
{
    /// <summary>An exercise at node id 1 ending at node id 5, enclosed by one at node id 0 ending at 2.</summary>
    public const string SubtreeOverlapsInsteadOfNesting =
        "Cannot reconstruct the transaction tree: the subtree of 'Straddles' at node id 1 ends at node id 5, "
        + "past the end (2) of the enclosing subtree rooted at node id 0, so the two overlap instead of nesting.";

    /// <summary>An exercise at node id 4 claiming a last descendant node id of 2.</summary>
    public const string SubtreeEndsBeforeItsExercise =
        "Cannot reconstruct the transaction tree: the exercise of 'Backwards' at node id 4 claims a "
        + "last descendant node id of 2, which precedes the exercise itself.";

    /// <summary>A second event at node id 3 following a first event at node id 3.</summary>
    public const string NodeIdsDoNotAscend =
        "Cannot reconstruct the transaction tree: node id 3 follows node id 3, "
        + "but node ids must strictly ascend.";
}
