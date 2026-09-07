// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;

namespace Canton.Ledger.Kernel.Results;

internal static class TransactionResultFolds
{
    internal static ExerciseOutcome<TProjection> Project<TProjection>(
        ExerciseOutcome<TransactionResult> outcome,
        Func<TransactionResult, ExerciseOutcome<TProjection>> projectSuccess)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return outcome switch
        {
            ExerciseOutcome<TransactionResult>.One success => projectSuccess(success.Result),
            ExerciseOutcome<TransactionResult>.DamlError damlError => new ExerciseOutcome<TProjection>.DamlError(
                damlError.Category, damlError.ErrorId, damlError.Message, damlError.Metadata),
            ExerciseOutcome<TransactionResult>.InfraError infraError => new ExerciseOutcome<TProjection>.InfraError(
                infraError.StatusCode, infraError.Message, infraError.Category, infraError.SourceException),
            _ => throw new InvalidOperationException($"Unhandled outcome: {outcome.GetType().Name}"),
        };
    }

    internal static ExerciseOutcome<ContractId<TMarker>> ToCreatedContractId<TMarker>(
        TransactionResult result,
        Func<CreatedContract, bool> matchesMarker)
        where TMarker : IDamlType
    {
        var matches = new List<string>();
        foreach (var created in result.CreatedContracts)
        {
            if (matchesMarker(created))
            {
                matches.Add(created.ContractId);
            }
        }

        return matches.Count switch
        {
            0 => new ExerciseOutcome<ContractId<TMarker>>.None(),
            1 => new ExerciseOutcome<ContractId<TMarker>>.One(new ContractId<TMarker>(matches[0])),
            _ => new ExerciseOutcome<ContractId<TMarker>>.Many(matches.Count, matches),
        };
    }

    internal static ExerciseOutcome<TResult> ToChoiceResult<TResult>(TransactionResult result, ChoiceName choice)
    {
        var choiceResult = result.ExerciseResult<TResult>(choice);

        return choiceResult is null
            ? new ExerciseOutcome<TResult>.None()
            : new ExerciseOutcome<TResult>.One(choiceResult);
    }
}
