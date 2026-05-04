// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Grpc;
using Daml.Runtime.Outcomes;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using RuntimeExercisedEvent = Daml.Runtime.Contracts.ExercisedEvent;

namespace Canton.Ledger.Grpc.Client;

internal static class TransactionResultProjector
{
    public static TransactionResult Project(SubmitAndWaitForTransactionResponse response)
    {
        var transaction = response.Transaction
            ?? throw new InvalidOperationException(
                "Server returned a successful response but no Transaction was present.");

        var createdContracts = new List<CreatedContract>();
        var archivedContractIds = new List<string>();
        var exercisedEvents = new List<RuntimeExercisedEvent>();

        foreach (var evt in transaction.Events)
        {
            switch (evt.EventCase)
            {
                case Event.EventOneofCase.Created:
                    createdContracts.Add(new CreatedContract(
                        evt.Created.ContractId,
                        LedgerWireConversions.ToRuntimeIdentifier(evt.Created.TemplateId),
                        evt.Created.CreateArguments.ToString()));
                    break;
                case Event.EventOneofCase.Archived:
                    archivedContractIds.Add(evt.Archived.ContractId);
                    break;
                case Event.EventOneofCase.Exercised:
                    exercisedEvents.Add(ToRuntimeExercisedEvent(evt.Exercised));
                    break;
            }
        }

        return new TransactionResult(
            transaction.UpdateId,
            transaction.Offset,
            createdContracts,
            archivedContractIds)
        {
            ExercisedEvents = exercisedEvents,
        };
    }

    public static ExerciseOutcome<ContractId<TTemplate>> ProjectToContractId<TTemplate>(
        ExerciseOutcome<TransactionResult> outcome)
        where TTemplate : ITemplate
    {
        return outcome switch
        {
            ExerciseOutcome<TransactionResult>.One success => ProjectSuccess<TTemplate>(success.Result),
            ExerciseOutcome<TransactionResult>.DamlError damlError => new ExerciseOutcome<ContractId<TTemplate>>.DamlError(
                damlError.Category, damlError.ErrorId, damlError.Message, damlError.Metadata),
            ExerciseOutcome<TransactionResult>.InfraError infraError => new ExerciseOutcome<ContractId<TTemplate>>.InfraError(
                infraError.StatusCode, infraError.Message),
            _ => throw new InvalidOperationException($"Unhandled outcome: {outcome.GetType().Name}"),
        };
    }

    private static ExerciseOutcome<ContractId<TTemplate>> ProjectSuccess<TTemplate>(TransactionResult result)
        where TTemplate : ITemplate
    {
        var matches = new List<string>();
        var expected = TTemplate.TemplateId;
        foreach (var c in result.CreatedContracts)
        {
            if (string.Equals(c.TemplateId.ModuleName, expected.ModuleName, StringComparison.Ordinal)
                && string.Equals(c.TemplateId.EntityName, expected.EntityName, StringComparison.Ordinal))
            {
                matches.Add(c.ContractId);
            }
        }

        return matches.Count switch
        {
            0 => new ExerciseOutcome<ContractId<TTemplate>>.None(),
            1 => new ExerciseOutcome<ContractId<TTemplate>>.One(new ContractId<TTemplate>(matches[0])),
            _ => new ExerciseOutcome<ContractId<TTemplate>>.Many(matches.Count, matches),
        };
    }

    private static RuntimeExercisedEvent ToRuntimeExercisedEvent(ProtoExercisedEvent exercised)
    {
        var argument = exercised.ChoiceArgument is null
            ? DamlUnit.Instance
            : DamlValueConverter.FromProtoValue(exercised.ChoiceArgument);
        var result = exercised.ExerciseResult is null
            ? DamlUnit.Instance
            : DamlValueConverter.FromProtoValue(exercised.ExerciseResult);
        var interfaceId = exercised.InterfaceId is null
            ? null
            : LedgerWireConversions.ToRuntimeIdentifier(exercised.InterfaceId);
        return new RuntimeExercisedEvent(
            exercised.ContractId,
            LedgerWireConversions.ToRuntimeIdentifier(exercised.TemplateId),
            interfaceId,
            exercised.Choice,
            argument,
            result,
            exercised.Consuming,
            LedgerWireConversions.ToPartyList(exercised.ActingParties),
            LedgerWireConversions.ToPartyList(exercised.WitnessParties));
    }
}
