// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

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
/// into a created contract id or a typed choice result. Mirrors the gRPC transport's
/// <c>TransactionResultProjector</c>.
/// </summary>
internal static class RestTransactionResultProjector
{
    public static TransactionResult Project(WireTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var createdContracts = new List<CreatedContract>();
        var archivedContractIds = new List<string>();
        var exercisedEvents = new List<ExercisedEvent>();

        foreach (var evt in transaction.Events ?? [])
        {
            if (evt?.CreatedEvent is { } created)
            {
                createdContracts.Add(ToCreatedContract(created));
            }
            else if (evt?.ArchivedEvent is { } archived)
            {
                archivedContractIds.Add(archived.ContractId);
            }
            else if (evt?.ExercisedEvent is { } exercised)
            {
                exercisedEvents.Add(ToExercisedEvent(exercised));
            }
        }

        return new TransactionResult(
            transaction.UpdateId,
            LedgerOffset.At(RestWireConversions.ParseOffset(transaction.Offset)),
            createdContracts,
            archivedContractIds,
            ToCommandId(transaction.CommandId))
        {
            ExercisedEvents = exercisedEvents,
        };
    }

    public static ExerciseOutcome<ContractId<TTemplate>> ProjectToContractId<TTemplate>(
        ExerciseOutcome<TransactionResult> outcome)
        where TTemplate : ITemplate
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return outcome switch
        {
            ExerciseOutcome<TransactionResult>.One success => ProjectCreatedContractId<TTemplate>(success.Result),
            ExerciseOutcome<TransactionResult>.DamlError damlError => new ExerciseOutcome<ContractId<TTemplate>>.DamlError(
                damlError.Category, damlError.ErrorId, damlError.Message, damlError.Metadata),
            ExerciseOutcome<TransactionResult>.InfraError infraError => new ExerciseOutcome<ContractId<TTemplate>>.InfraError(
                infraError.StatusCode, infraError.Message),
            _ => throw new InvalidOperationException($"Unhandled outcome: {outcome.GetType().Name}"),
        };
    }

    public static ExerciseOutcome<TResult> ProjectChoiceResult<TResult>(
        ExerciseOutcome<TransactionResult> outcome, ChoiceName choice)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return outcome switch
        {
            ExerciseOutcome<TransactionResult>.One success => new ExerciseOutcome<TResult>.One(
                ExerciseResult<TResult>(success.Result, choice.Value)!),
            ExerciseOutcome<TransactionResult>.DamlError damlError => new ExerciseOutcome<TResult>.DamlError(
                damlError.Category, damlError.ErrorId, damlError.Message, damlError.Metadata),
            ExerciseOutcome<TransactionResult>.InfraError infraError => new ExerciseOutcome<TResult>.InfraError(
                infraError.StatusCode, infraError.Message),
            _ => throw new InvalidOperationException($"Unhandled outcome: {outcome.GetType().Name}"),
        };
    }

    private static TResult? ExerciseResult<TResult>(TransactionResult result, string choiceName)
    {
        var matches = new List<ExercisedEvent>(result.ExercisedEvents.Count);
        foreach (var exercised in result.ExercisedEvents)
        {
            if (string.Equals(exercised.ChoiceName, choiceName, StringComparison.Ordinal))
            {
                matches.Add(exercised);
            }
        }

        return matches.Count switch
        {
            1 => matches[0].ExerciseResult.FromDamlValue<TResult>(),
            0 => throw new InvalidOperationException(
                $"Transaction contains no exercised event for choice '{choiceName}'."),
            _ => throw new InvalidOperationException(
                $"Transaction contains {matches.Count} exercised events for choice '{choiceName}', expected exactly 1."),
        };
    }

    private static ExerciseOutcome<ContractId<TTemplate>> ProjectCreatedContractId<TTemplate>(TransactionResult result)
        where TTemplate : ITemplate
    {
        var matches = new List<string>();
        foreach (var created in result.CreatedContracts)
        {
            if (MarkerMatcher<TTemplate>.MatchesContract(created))
            {
                matches.Add(created.ContractId);
            }
        }

        return matches.Count switch
        {
            0 => new ExerciseOutcome<ContractId<TTemplate>>.None(),
            1 => new ExerciseOutcome<ContractId<TTemplate>>.One(new ContractId<TTemplate>(matches[0])),
            _ => new ExerciseOutcome<ContractId<TTemplate>>.Many(matches.Count, matches),
        };
    }

    private static CommandId ToCommandId(string? commandId) =>
        string.IsNullOrEmpty(commandId) ? default : (CommandId)commandId;

    private static CreatedContract ToCreatedContract(WireCreatedEvent created)
    {
        var templateId = created.TemplateId
            ?? throw MalformedResponse($"CreatedEvent for contract '{created.ContractId}' has no templateId");
        var payload = DamlJsonSerializer.Serialize(RestValueDecoder.ToDamlRecord(created.CreateArgument));

        return new CreatedContract(created.ContractId, ToRuntimeIdentifier(templateId), payload)
        {
            InterfaceIds = ToInterfaceIds(created),
        };
    }

    private static IReadOnlyList<RuntimeIdentifier> ToInterfaceIds(WireCreatedEvent created)
    {
        if (created.InterfaceViews is not { Count: > 0 } views)
        {
            return [];
        }

        var interfaceIds = new List<RuntimeIdentifier>(views.Count);
        foreach (var view in views)
        {
            var interfaceId = view?.InterfaceId
                ?? throw MalformedResponse(
                    $"an interface view on CreatedEvent for contract '{created.ContractId}' has no interfaceId");
            interfaceIds.Add(ToRuntimeIdentifier(interfaceId));
        }
        return interfaceIds;
    }

    private static ExercisedEvent ToExercisedEvent(WireExercisedEvent exercised)
    {
        var templateId = exercised.TemplateId
            ?? throw MalformedResponse($"ExercisedEvent for contract '{exercised.ContractId}' has no templateId");
        var choiceArgument = exercised.ChoiceArgument is null
            ? DamlUnit.Instance
            : RestValueDecoder.ToDamlValue(exercised.ChoiceArgument);
        var result = exercised.ExerciseResult is null
            ? DamlUnit.Instance
            : RestValueDecoder.ToDamlValue(exercised.ExerciseResult);
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

    internal const string MalformedResponsePrefix = "Malformed response from ledger: ";

    internal static InvalidOperationException MalformedResponse(string detail) =>
        new($"{MalformedResponsePrefix}{detail}, though the Ledger API marks the field as required.");

    /// <summary>
    /// True for an <see cref="InvalidOperationException"/> raised during response projection to signal a
    /// malformed wire body — either by <see cref="MalformedResponse"/> in this projector or by
    /// <see cref="RestValueDecoder"/>'s equivalent required-field guards, which share
    /// <see cref="MalformedResponsePrefix"/>. Callers use this to distinguish a genuinely malformed wire body
    /// from an unrelated <see cref="InvalidOperationException"/> that a downstream bug might otherwise raise,
    /// so the latter is not silently masked as an infrastructure error.
    /// </summary>
    public static bool IsMalformedResponse(Exception exception) =>
        exception is InvalidOperationException { Message: { } message }
        && message.StartsWith(MalformedResponsePrefix, StringComparison.Ordinal);
}
