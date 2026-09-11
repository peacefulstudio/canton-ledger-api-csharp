// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Results;
using Canton.Ledger.Kernel.Trees;
using Canton.Ledger.Kernel.Wire;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using ChoiceName = Daml.Runtime.Commands.ChoiceName;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using RuntimeExercisedEvent = Daml.Runtime.Contracts.ExercisedEvent;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client;

internal static class GrpcTransactionResultProjector
{
    public static TransactionResult Project(SubmitAndWaitForTransactionResponse response)
    {
        var transaction = response.Transaction
            ?? throw new InvalidOperationException(
                "Server returned a successful response but no Transaction was present.");
        return Project(transaction);
    }

    public static TransactionResult Project(Transaction transaction)
    {
        var createdContracts = new List<CreatedContract>();
        var archivedContractIds = new List<string>();
        var exercisedEvents = new List<RuntimeExercisedEvent>();

        foreach (var evt in transaction.Events)
        {
            switch (evt.EventCase)
            {
                case Event.EventOneofCase.Created:
                    createdContracts.Add(ToCreatedContract(evt.Created));
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
            LedgerWireConversions.ToLedgerOffset(transaction.Offset),
            EquatableArray.Create(createdContracts),
            EquatableArray.Create(archivedContractIds),
            LedgerWireConversions.ToCommandId(transaction.CommandId))
        {
            ExercisedEvents = EquatableArray.Create(exercisedEvents),
        };
    }

    private static CreatedContract ToCreatedContract(ProtoCreatedEvent created)
    {
        var templateId = created.TemplateId
            ?? throw MalformedResponse.MissingRequiredField(
                $"CreatedEvent for contract '{created.ContractId}' has no template_id");
        var createArguments = created.CreateArguments
            ?? throw MalformedResponse.MissingRequiredField(
                $"CreatedEvent for contract '{created.ContractId}' has no create_arguments");
        var runtimeTemplateId = LedgerWireConversions.ToRuntimeIdentifier(templateId);
        return new CreatedContract(
            TreeShape.EventIdOf(created.NodeId),
            created.ContractId,
            runtimeTemplateId,
            GrpcValueDecoder.ToDamlRecord(createArguments),
            LedgerWireConversions.ToPartyList(created.WitnessParties),
            LedgerWireConversions.ToPartyList(created.Signatories),
            LedgerWireConversions.ToPartyList(created.Observers),
            ContractKey: LedgerWireConversions.ContractKeyOf(created, runtimeTemplateId),
            CreatedAt: GrpcValueDecoder.ToCreatedAt(created))
        {
            InterfaceIds = ToInterfaceIds(created),
        };
    }

    public static ExerciseOutcome<ContractId<TMarker>> ProjectToContractId<TMarker>(
        ExerciseOutcome<TransactionResult> outcome)
        where TMarker : IDamlType =>
        TransactionResultFolds.Project(
            outcome,
            result => TransactionResultFolds.ToCreatedContractId<TMarker>(result, MarkerMatcher<TMarker>.Matches));

    public static ExerciseOutcome<TResult> ProjectChoiceResult<TResult>(
        ExerciseOutcome<TransactionResult> outcome, ChoiceName choice) =>
        TransactionResultFolds.Project(
            outcome,
            result => TransactionResultFolds.ToChoiceResult<TResult>(result, choice));

    private static EquatableArray<RuntimeIdentifier> ToInterfaceIds(ProtoCreatedEvent created)
    {
        if (created.InterfaceViews.Count == 0)
        {
            return [];
        }

        var interfaceIds = new List<RuntimeIdentifier>(created.InterfaceViews.Count);
        foreach (var view in created.InterfaceViews)
        {
            var interfaceId = view.InterfaceId
                ?? throw MalformedResponse.MissingRequiredField(
                    $"an interface view on CreatedEvent for contract '{created.ContractId}' has no interface_id");
            interfaceIds.Add(LedgerWireConversions.ToRuntimeIdentifier(interfaceId));
        }
        return EquatableArray.Create(interfaceIds);
    }

    private static RuntimeExercisedEvent ToRuntimeExercisedEvent(ProtoExercisedEvent exercised)
    {
        var templateId = exercised.TemplateId
            ?? throw MalformedResponse.MissingRequiredField(
                $"ExercisedEvent for contract '{exercised.ContractId}' has no template_id");
        var argument = exercised.ChoiceArgument is null
            ? DamlUnit.Instance
            : GrpcValueDecoder.ToDamlValue(exercised.ChoiceArgument);
        var result = exercised.ExerciseResult is null
            ? DamlUnit.Instance
            : GrpcValueDecoder.ToDamlValue(exercised.ExerciseResult);
        var interfaceId = exercised.InterfaceId is null
            ? null
            : LedgerWireConversions.ToRuntimeIdentifier(exercised.InterfaceId);
        return new RuntimeExercisedEvent(
            exercised.ContractId,
            LedgerWireConversions.ToRuntimeIdentifier(templateId),
            interfaceId,
            exercised.Choice,
            argument,
            result,
            exercised.Consuming,
            LedgerWireConversions.ToPartyList(exercised.ActingParties),
            LedgerWireConversions.ToPartyList(exercised.WitnessParties));
    }
}
