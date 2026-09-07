// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestChoiceResultProjectionParityTests : ChoiceResultProjectionParityTests
{
    protected override ExerciseOutcome<TResult> ProjectChoiceResult<TResult>(
        ExerciseOutcome<TransactionResult> outcome, ChoiceName choice) =>
        RestTransactionResultProjector.ProjectChoiceResult<TResult>(outcome, choice);
}
