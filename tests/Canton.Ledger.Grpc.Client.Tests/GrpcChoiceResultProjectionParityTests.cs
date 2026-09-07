// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class GrpcChoiceResultProjectionParityTests : ChoiceResultProjectionParityTests
{
    protected override ExerciseOutcome<TResult> ProjectChoiceResult<TResult>(
        ExerciseOutcome<TransactionResult> outcome, ChoiceName choice) =>
        GrpcTransactionResultProjector.ProjectChoiceResult<TResult>(outcome, choice);
}
