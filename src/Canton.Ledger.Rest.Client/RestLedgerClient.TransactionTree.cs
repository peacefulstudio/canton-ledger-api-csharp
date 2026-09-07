// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Rest.Client;

internal sealed partial class RestLedgerClient
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

    /// <inheritdoc />
    /// <remarks>
    /// The participant reports the hierarchy implicitly, as node ids on the events of the ordinary
    /// ledger-effects response, so the tree is rebuilt from the same response the flat point read
    /// decodes rather than from a second request. Node ids that cannot describe a tree surface as a
    /// <see cref="MalformedTransactionTreeException"/> carried in
    /// <see cref="Exception.InnerException"/>.
    /// </remarks>
    public Task<TransactionTree> GetUpdateTreeByOffsetAsync(
        long offset,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(offset);

        var request = new Raw.GetUpdateByOffsetRequest
        {
            Offset = offset.ToString(CultureInfo.InvariantCulture),
            UpdateFormat = RestSubscribeRequestBuilder.BuildTransactionUpdateFormat(submitter),
        };

        return GetUpdateAsync(
            UpdateByOffsetPath, request, $"offset {offset}", RestTransactionTreeProjector.Project,
            timeout, cancellationToken);
    }
}
