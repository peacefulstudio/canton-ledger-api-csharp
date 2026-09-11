// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Results;
using Canton.Ledger.Kernel.Trees;
using Canton.Ledger.Kernel.Wire;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using WireCreatedEvent = Canton.Ledger.Rest.Client.Raw.CreatedEvent;
using WireExercisedEvent = Canton.Ledger.Rest.Client.Raw.ExercisedEvent;
using WireIdentifier = Canton.Ledger.Rest.Client.Raw.Identifier;
using WireTransaction = Canton.Ledger.Rest.Client.Raw.Transaction;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Projects a decoded wire <see cref="WireTransaction"/> into the transport-neutral
/// <see cref="TransactionResult"/>, and projects a <see cref="TransactionResult"/> outcome further
/// into a created contract id or a typed choice result. The outcome folds live in
/// <see cref="TransactionResultFolds"/>, which the gRPC transport's
/// <c>GrpcTransactionResultProjector</c> calls with its own marker matcher and the same choice
/// name, so both transports fold an outcome the same way — including reporting a choice return
/// that decodes to <c>null</c> as <c>None</c> — and differ only in wire vocabulary.
/// </summary>
internal static class RestTransactionResultProjector
{
    public static TransactionResult Project(WireTransaction transaction) =>
        ProjectCore(transaction, DecodeExerciseResultUntyped, DecodeCreateArgumentUntyped, DecodeContractKeyUntyped);

    public static TransactionResult ProjectForChoiceResult<TResult>(
        WireTransaction transaction, ChoiceName choice) =>
        ProjectCore(transaction, ExerciseResultDecoderFor<TResult>(choice), DecodeCreateArgumentUntyped, DecodeContractKeyUntyped);

    public static TransactionResult ProjectForCreatedTemplate<TTemplate>(WireTransaction transaction)
        where TTemplate : ITemplate =>
        ProjectCore(
            transaction,
            DecodeExerciseResultUntyped,
            CreateArgumentDecoderFor<TTemplate>(),
            ContractKeyDecoderFor<TTemplate>());

    private static TransactionResult ProjectCore(
        WireTransaction transaction,
        Func<WireExercisedEvent, DamlValue> decodeExerciseResult,
        Func<WireCreatedEvent, DamlRecord> decodeCreateArgument,
        Func<WireCreatedEvent, RuntimeIdentifier, ContractKey?> decodeContractKey)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var createdContracts = new List<CreatedContract>();
        var archivedContractIds = new List<string>();
        var exercisedEvents = new List<ExercisedEvent>();

        foreach (var evt in transaction.Events ?? [])
        {
            if (evt?.CreatedEvent is { } created)
            {
                createdContracts.Add(ToCreatedContract(created, decodeCreateArgument, decodeContractKey));
            }
            else if (evt?.ArchivedEvent is { } archived)
            {
                archivedContractIds.Add(archived.ContractId);
            }
            else if (evt?.ExercisedEvent is { } exercised)
            {
                exercisedEvents.Add(ToExercisedEvent(exercised, decodeExerciseResult));
            }
        }

        return new TransactionResult(
            transaction.UpdateId,
            LedgerOffset.At(RestWireConversions.ParseOffset(transaction.Offset)),
            EquatableArray.Create(createdContracts),
            EquatableArray.Create(archivedContractIds),
            ToCommandId(transaction.CommandId))
        {
            ExercisedEvents = EquatableArray.Create(exercisedEvents),
        };
    }

    private static DamlValue DecodeExerciseResultUntyped(WireExercisedEvent exercised) =>
        exercised.ExerciseResult is null ? DamlUnit.Instance : RestValueDecoder.ToDamlValue(exercised.ExerciseResult);

    private static DamlRecord DecodeCreateArgumentUntyped(WireCreatedEvent created) =>
        RestValueDecoder.ToDamlRecord(created.CreateArgument);

    private static ContractKey? DecodeContractKeyUntyped(WireCreatedEvent created, RuntimeIdentifier runtimeTemplateId) =>
        RestWireConversions.ContractKeyOf(created, runtimeTemplateId);

    private static Func<WireExercisedEvent, DamlValue> ExerciseResultDecoderFor<TResult>(ChoiceName choice) =>
        exercised => exercised.ExerciseResult is null
            ? DamlUnit.Instance
            : string.Equals(exercised.Choice, choice.Value, StringComparison.Ordinal)
                ? RestValueDecoder.ToDamlValue<TResult>(exercised.ExerciseResult)
                : RestValueDecoder.ToDamlValue(exercised.ExerciseResult);

    private static Func<WireCreatedEvent, DamlRecord> CreateArgumentDecoderFor<TTemplate>()
        where TTemplate : ITemplate =>
        created => MarkerMatcher<TTemplate>.MatchesCreated(created)
            ? RestValueDecoder.ToDamlRecord<TTemplate>(created.CreateArgument)
            : RestValueDecoder.ToDamlRecord(created.CreateArgument);

    private static Func<WireCreatedEvent, RuntimeIdentifier, ContractKey?> ContractKeyDecoderFor<TTemplate>()
        where TTemplate : ITemplate =>
        (created, runtimeTemplateId) => RestWireConversions.ContractKeyOf(
            created, runtimeTemplateId, MarkerMatcher<TTemplate>.MatchesCreated(created) ? MarkerMatcher<TTemplate>.KeyType : null);

    public static ExerciseOutcome<ContractId<TTemplate>> ProjectToContractId<TTemplate>(
        ExerciseOutcome<TransactionResult> outcome)
        where TTemplate : ITemplate =>
        TransactionResultFolds.Project(
            outcome,
            result => TransactionResultFolds.ToCreatedContractId<TTemplate>(
                result, MarkerMatcher<TTemplate>.MatchesContract));

    public static ExerciseOutcome<TResult> ProjectChoiceResult<TResult>(
        ExerciseOutcome<TransactionResult> outcome, ChoiceName choice) =>
        TransactionResultFolds.Project(
            outcome,
            result => TransactionResultFolds.ToChoiceResult<TResult>(result, choice));

    private static CommandId? ToCommandId(string? commandId) =>
        string.IsNullOrEmpty(commandId) ? null : MalformedResponse.Decoding(commandId, ToNamedCommandId);

    private static CommandId ToNamedCommandId(string commandId) => (CommandId)commandId;

    private static CreatedContract ToCreatedContract(
        WireCreatedEvent created,
        Func<WireCreatedEvent, DamlRecord> decodeCreateArgument,
        Func<WireCreatedEvent, RuntimeIdentifier, ContractKey?> decodeContractKey)
    {
        var templateId = created.TemplateId
            ?? throw MalformedResponse.MissingRequiredField(
                $"CreatedEvent for contract '{created.ContractId}' has no templateId");
        var nodeId = created.NodeId
            ?? throw MalformedResponse.MissingRequiredField(
                $"CreatedEvent for contract '{created.ContractId}' has no nodeId");
        var runtimeTemplateId = ToRuntimeIdentifier(templateId);

        return new CreatedContract(
            TreeShape.EventIdOf(nodeId),
            created.ContractId,
            runtimeTemplateId,
            decodeCreateArgument(created),
            RestWireConversions.ToPartyList(created.WitnessParties),
            RestWireConversions.ToPartyList(created.Signatories),
            RestWireConversions.ToPartyList(created.Observers),
            ContractKey: decodeContractKey(created, runtimeTemplateId),
            CreatedAt: created.CreatedAt)
        {
            InterfaceIds = ToInterfaceIds(created),
        };
    }

    private static EquatableArray<RuntimeIdentifier> ToInterfaceIds(WireCreatedEvent created)
    {
        if (created.InterfaceViews is not { Count: > 0 } views)
        {
            return [];
        }

        var interfaceIds = new List<RuntimeIdentifier>(views.Count);
        foreach (var view in views)
        {
            var interfaceId = view?.InterfaceId
                ?? throw MalformedResponse.MissingRequiredField(
                    $"an interface view on CreatedEvent for contract '{created.ContractId}' has no interfaceId");
            interfaceIds.Add(ToRuntimeIdentifier(interfaceId));
        }
        return EquatableArray.Create(interfaceIds);
    }

    private static ExercisedEvent ToExercisedEvent(
        WireExercisedEvent exercised, Func<WireExercisedEvent, DamlValue> decodeExerciseResult)
    {
        var templateId = exercised.TemplateId
            ?? throw MalformedResponse.MissingRequiredField(
                $"ExercisedEvent for contract '{exercised.ContractId}' has no templateId");
        var choiceArgument = exercised.ChoiceArgument is null
            ? DamlUnit.Instance
            : RestValueDecoder.ToDamlValue(exercised.ChoiceArgument);
        var result = decodeExerciseResult(exercised);
        var interfaceId = exercised.InterfaceId is null ? null : ToRuntimeIdentifier(exercised.InterfaceId);

        return new ExercisedEvent(
            exercised.ContractId,
            ToRuntimeIdentifier(templateId),
            interfaceId,
            exercised.Choice,
            choiceArgument,
            result,
            exercised.Consuming ?? false,
            RestWireConversions.ToPartyList(exercised.ActingParties),
            RestWireConversions.ToPartyList(exercised.WitnessParties));
    }

    private static RuntimeIdentifier ToRuntimeIdentifier(WireIdentifier identifier) =>
        RestWireConversions.ToRuntimeIdentifier(identifier);
}
