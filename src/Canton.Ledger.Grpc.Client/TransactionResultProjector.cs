// Copyright 2026 Peaceful Studio OÜ

using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Grpc;
using Daml.Runtime.Outcomes;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using RuntimeExercisedEvent = Daml.Runtime.Contracts.ExercisedEvent;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

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
                        evt.Created.CreateArguments.ToString())
                    {
                        InterfaceIds = ToInterfaceIds(evt.Created),
                    });
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

    public static ExerciseOutcome<ContractId<TMarker>> ProjectToContractId<TMarker>(
        ExerciseOutcome<TransactionResult> outcome)
        where TMarker : IDamlType
    {
        return outcome switch
        {
            ExerciseOutcome<TransactionResult>.One success => ProjectSuccess<TMarker>(success.Result),
            ExerciseOutcome<TransactionResult>.DamlError damlError => new ExerciseOutcome<ContractId<TMarker>>.DamlError(
                damlError.Category, damlError.ErrorId, damlError.Message, damlError.Metadata),
            ExerciseOutcome<TransactionResult>.InfraError infraError => new ExerciseOutcome<ContractId<TMarker>>.InfraError(
                infraError.StatusCode, infraError.Message),
            _ => throw new InvalidOperationException($"Unhandled outcome: {outcome.GetType().Name}"),
        };
    }

    private static ExerciseOutcome<ContractId<TMarker>> ProjectSuccess<TMarker>(TransactionResult result)
        where TMarker : IDamlType
    {
        var matches = new List<string>();
        foreach (var c in result.CreatedContracts)
        {
            if (MarkerMatcher<TMarker>.Matches(c))
            {
                matches.Add(c.ContractId);
            }
        }

        return matches.Count switch
        {
            0 => new ExerciseOutcome<ContractId<TMarker>>.None(),
            1 => new ExerciseOutcome<ContractId<TMarker>>.One(new ContractId<TMarker>(matches[0])),
            _ => new ExerciseOutcome<ContractId<TMarker>>.Many(matches.Count, matches),
        };
    }

    private static IReadOnlyList<RuntimeIdentifier> ToInterfaceIds(ProtoCreatedEvent created)
    {
        if (created.InterfaceViews.Count == 0)
        {
            return [];
        }

        var interfaceIds = new List<RuntimeIdentifier>(created.InterfaceViews.Count);
        foreach (var view in created.InterfaceViews)
        {
            interfaceIds.Add(LedgerWireConversions.ToRuntimeIdentifier(view.InterfaceId));
        }
        return interfaceIds;
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
