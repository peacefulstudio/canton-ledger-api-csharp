// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Rest.Client;

public sealed partial class RestLedgerClient
{
    /// <inheritdoc />
    /// <remarks>
    /// The participant reports the hierarchy implicitly, as node ids on the events of the ordinary
    /// ledger-effects response: each exercise states the highest node id in the subtree it caused, so
    /// the subtree rooted at an exercise is exactly that node-id interval and the tree is rebuilt from
    /// one response rather than a second request. Node-id gaps left by the participant's own party
    /// filtering are normal and tolerated.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="submission"/> is <see langword="null"/>.</exception>
    public Task<ExerciseOutcome<TransactionTree>> TrySubmitAndWaitForTransactionTreeAsync(
        RuntimeCommands.CommandsSubmission submission,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        return TrySubmitAndWaitForTransactionCoreAsync(
            submission.WithSubmitter(submitter),
            RestSubscribeRequestBuilder.BuildTransactionFormat(submitter),
            RestTransactionTreeProjector.Project,
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// Reads the transaction committed at <paramref name="offset"/> and returns it with its
    /// parent/child hierarchy intact, as the tree-shaped counterpart to
    /// <see cref="GetUpdateByOffsetAsync"/>.
    /// </summary>
    /// <remarks>
    /// Always reads the ledger-effects view, because hierarchy is only meaningful over creates and
    /// exercises. An update at that offset which is not a transaction, or whose node ids cannot
    /// describe a tree, throws <see cref="InvalidOperationException"/> rather than yielding a
    /// partial or silently wrong tree.
    /// </remarks>
    /// <param name="offset">The absolute offset to read, which must be positive.</param>
    /// <param name="submitter">The parties to read the transaction's events as.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The transaction committed at <paramref name="offset"/>, with its hierarchy intact.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is zero or negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// The update at that offset is not a transaction, or its events could not be decoded. Node ids that
    /// cannot describe a tree surface here as a <see cref="MalformedTransactionTreeException"/> carried in
    /// <see cref="Exception.InnerException"/>, so catch this base type rather than the derived one.
    /// </exception>
    public Task<TransactionTree> GetUpdateTreeByOffsetAsync(
        long offset,
        RuntimeCommands.SubmitterInfo submitter,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(offset);

        var request = new Raw.GetUpdateByOffsetRequest
        {
            Offset = offset.ToString(CultureInfo.InvariantCulture),
            UpdateFormat = RestSubscribeRequestBuilder.BuildTransactionUpdateFormat(submitter),
        };

        return GetUpdateAsync(
            UpdateByOffsetPath, request, $"offset {offset}", RestTransactionTreeProjector.Project, cancellationToken);
    }
}
