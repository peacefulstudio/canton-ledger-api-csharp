// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client;

public sealed partial class LedgerClient
{
    /// <inheritdoc />
    /// <remarks>
    /// The participant reports the hierarchy on the ledger-effects transaction this method asks for,
    /// as the child events nested under each exercise. A duplicate submission the participant
    /// deduplicates is resolved through the tree-shaped point read, so a retried command still yields
    /// the committed transaction rather than a rejection.
    /// </remarks>
    public Task<ExerciseOutcome<TransactionTree>> TrySubmitAndWaitForTransactionTreeAsync(
        RuntimeCommands.CommandsSubmission submission,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        _submissionClient.TrySubmitAndWaitForTransactionTreeAsync(submission, submitter, timeout, cancellationToken);
}
