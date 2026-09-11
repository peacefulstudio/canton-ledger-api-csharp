// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Streams;
using Canton.Ledger.Rest.Client.Raw;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using WireCreatedEvent = Canton.Ledger.Rest.Client.Raw.CreatedEvent;
using WireEvent = Canton.Ledger.Rest.Client.Raw.Event;
using WireReassignment = Canton.Ledger.Rest.Client.Raw.Reassignment;
using WireReassignmentEvent = Canton.Ledger.Rest.Client.Raw.ReassignmentEvent;
using WireTransaction = Canton.Ledger.Rest.Client.Raw.Transaction;
using WireUnassignedEvent = Canton.Ledger.Rest.Client.Raw.UnassignedEvent;

namespace Canton.Ledger.Rest.Client;

internal static class InterfaceStreamProjector
{
    private const string EmptyTransactionEventRawKind = "empty-event";
    private const string EmptyReassignmentEventRawKind = "empty-reassignment-event";

    public static IEnumerable<InterfaceStreamEvent<TInterface, TView>> ProjectTransactionEvents<TInterface, TView>(
        WireTransaction transaction,
        ILogger? logger = null)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        if (!RestWireConversions.TryParseOffset(transaction.Offset, out var transactionOffset))
        {
            yield return Refused<TInterface, TView>(
                ContractStreamProjector.UnparseableOffsetRefusal(typeof(TInterface).Name, transaction.Offset, logger));
            yield break;
        }

        var synchronizerId = StreamEventClassifier.Synchronizer(transaction.SynchronizerId);

        foreach (var evt in transaction.Events ?? [])
        {
            InterfaceStreamEvent<TInterface, TView> projected;
            try
            {
                projected = ProjectTransactionEvent<TInterface, TView>(evt, synchronizerId, transactionOffset);
            }
            catch (Exception decodeFailure) when (StreamEventClassifier.IsNotCancellation(decodeFailure))
            {
                projected = Refused<TInterface, TView>(
                    DecodeFailure<TInterface>(transactionOffset, logger, decodeFailure));
            }
            yield return projected;
        }
    }

    private static InterfaceStreamEvent<TInterface, TView> ProjectTransactionEvent<TInterface, TView>(
        WireEvent evt,
        SynchronizerId? synchronizerId,
        long transactionOffset)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        if (evt?.CreatedEvent is { } created)
        {
            var offset = RestWireConversions.ParseOffset(created.Offset);
            ContractStreamProjector.RequireTemplateId(created.TemplateId, nameof(Raw.CreatedEvent), created.ContractId);
            var decoded = new DecodedStreamEvent<SynchronizerId>(
                offset,
                MarkerMatcher<TInterface>.MatchesCreated(created),
                synchronizerId,
                UnclassifiedKind.CreatedEvent);
            if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
            {
                return Refused<TInterface, TView>(refusal);
            }
            return CreatedFromWire<TInterface, TView>(created, scope, offset);
        }

        if (evt?.ArchivedEvent is { } archived)
        {
            var offset = RestWireConversions.ParseOffset(archived.Offset);
            ContractStreamProjector.RequireTemplateId(archived.TemplateId, nameof(Raw.ArchivedEvent), archived.ContractId);
            var decoded = new DecodedStreamEvent<SynchronizerId>(
                offset,
                MarkerMatcher<TInterface>.MatchesArchived(archived),
                synchronizerId,
                UnclassifiedKind.ArchivedEvent);
            if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
            {
                return Refused<TInterface, TView>(refusal);
            }
            var archivedContractId = archived.ContractId
                ?? throw new InvalidOperationException("Archived event has no contract id.");
            return new InterfaceStreamEvent<TInterface, TView>.Archived(
                new ContractId<TInterface>(archivedContractId),
                LedgerOffset.At(offset),
                scope,
                RestWireConversions.ToPartyList(archived.WitnessParties));
        }

        if (evt?.ExercisedEvent is { } exercised)
        {
            var offset = RestWireConversions.ParseOffset(exercised.Offset);
            ContractStreamProjector.RequireTemplateId(exercised.TemplateId, nameof(Raw.ExercisedEvent), exercised.ContractId);
            var decoded = new DecodedStreamEvent<SynchronizerId>(
                offset,
                MarkerMatcher<TInterface>.MatchesExercised(exercised),
                synchronizerId,
                UnclassifiedKind.ExercisedEvent);
            if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
            {
                return Refused<TInterface, TView>(refusal);
            }
            var exercisedContractId = exercised.ContractId
                ?? throw new InvalidOperationException("Exercised event has no contract id.");
            var argument = exercised.ChoiceArgument is null
                ? DamlUnit.Instance
                : RestValueDecoder.ToDamlValue(exercised.ChoiceArgument);
            var result = exercised.ExerciseResult is null
                ? DamlUnit.Instance
                : RestValueDecoder.ToDamlValue(exercised.ExerciseResult);
            return new InterfaceStreamEvent<TInterface, TView>.Exercised(
                new ContractId<TInterface>(exercisedContractId),
                exercised.Choice ?? string.Empty,
                argument,
                result,
                exercised.Consuming ?? false,
                LedgerOffset.At(offset),
                scope,
                RestWireConversions.ToPartyList(exercised.WitnessParties));
        }

        return new InterfaceStreamEvent<TInterface, TView>.Unclassified(
            LedgerOffset.At(transactionOffset), UnclassifiedKind.Unknown, EmptyTransactionEventRawKind);
    }

    public static IEnumerable<InterfaceStreamEvent<TInterface, TView>> ProjectReassignmentEvents<TInterface, TView>(
        WireReassignment reassignment,
        ILogger? logger = null)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        if (!RestWireConversions.TryParseOffset(reassignment.Offset, out var reassignmentOffset))
        {
            yield return Refused<TInterface, TView>(
                ContractStreamProjector.UnparseableOffsetRefusal(typeof(TInterface).Name, reassignment.Offset, logger));
            yield break;
        }

        foreach (var evt in reassignment.Events ?? [])
        {
            InterfaceStreamEvent<TInterface, TView> projected;
            try
            {
                projected = ProjectReassignmentEvent<TInterface, TView>(evt, reassignmentOffset);
            }
            catch (Exception decodeFailure) when (StreamEventClassifier.IsNotCancellation(decodeFailure))
            {
                projected = Refused<TInterface, TView>(
                    DecodeFailure<TInterface>(reassignmentOffset, logger, decodeFailure));
            }
            yield return projected;
        }
    }

    private static InterfaceStreamEvent<TInterface, TView> ProjectReassignmentEvent<TInterface, TView>(
        WireReassignmentEvent evt,
        long reassignmentOffset)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        if (evt?.JsAssignmentEvent is { } assigned)
        {
            var created = assigned.CreatedEvent;
            if (created is null)
            {
                return new InterfaceStreamEvent<TInterface, TView>.Unclassified(
                    LedgerOffset.At(reassignmentOffset), UnclassifiedKind.AssignedEvent);
            }
            var createdOffset = RestWireConversions.ParseOffset(created.Offset);
            ContractStreamProjector.RequireTemplateId(created.TemplateId, nameof(Raw.CreatedEvent), created.ContractId);
            var decoded = new DecodedStreamEvent<ReassignmentScope>(
                createdOffset,
                MarkerMatcher<TInterface>.MatchesCreated(created),
                StreamEventClassifier.ReassignmentSynchronizers(assigned.Source, assigned.Target),
                UnclassifiedKind.AssignedEvent);
            if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
            {
                return Refused<TInterface, TView>(refusal);
            }
            var assignedContractId = created.ContractId
                ?? throw new InvalidOperationException("Assigned event's created event has no contract id.");
            if (!TryResolveView<TInterface, TView>(created, out var view))
            {
                var unavailableViewOffset = createdOffset > 0 ? createdOffset : reassignmentOffset;
                return new InterfaceStreamEvent<TInterface, TView>.Unclassified(
                    LedgerOffset.At(unavailableViewOffset), UnclassifiedKind.InterfaceViewUnavailable);
            }
            return new InterfaceStreamEvent<TInterface, TView>.Assigned(
                new ContractId<TInterface>(assignedContractId),
                view,
                ContractStreamProjector.ContractKeyOf<TInterface>(created),
                LedgerOffset.At(createdOffset),
                scope.Source,
                scope.Target,
                assigned.ReassignmentId ?? string.Empty,
                RestWireConversions.ParseReassignmentCounter(assigned.ReassignmentCounter),
                RestWireConversions.ToPartyList(created.WitnessParties));
        }

        if (evt?.JsUnassignedEvent is { } unassigned)
        {
            var offset = RestWireConversions.ParseOffset(unassigned.Offset);
            ContractStreamProjector.RequireTemplateId(unassigned.TemplateId, nameof(Raw.UnassignedEvent), unassigned.ContractId);
            var decoded = new DecodedStreamEvent<ReassignmentScope>(
                offset,
                MarkerMatcher<TInterface>.MatchesUnassigned(unassigned),
                StreamEventClassifier.ReassignmentSynchronizers(unassigned.Source, unassigned.Target),
                UnclassifiedKind.UnassignedEvent);
            if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
            {
                return Refused<TInterface, TView>(refusal);
            }
            var unassignedContractId = unassigned.ContractId
                ?? throw new InvalidOperationException("Unassigned event has no contract id.");
            return new InterfaceStreamEvent<TInterface, TView>.Unassigned(
                new ContractId<TInterface>(unassignedContractId),
                LedgerOffset.At(offset),
                scope.Source,
                scope.Target,
                unassigned.ReassignmentId ?? string.Empty,
                RestWireConversions.ParseReassignmentCounter(unassigned.ReassignmentCounter),
                RestWireConversions.ToPartyList(unassigned.WitnessParties));
        }

        return new InterfaceStreamEvent<TInterface, TView>.Unclassified(
            LedgerOffset.At(reassignmentOffset), UnclassifiedKind.Unknown, EmptyReassignmentEventRawKind);
    }

    public static IEnumerable<InterfaceStreamEvent<TInterface, TView>> ProjectActiveContractEntry<TInterface, TView>(
        GetActiveContractsResponse response,
        ILogger? logger = null,
        LedgerOffset? snapshotOffset = null)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        var (created, wireSynchronizerId, unassignmentWireOffset, rawEntryKind) = response.ContractEntry switch
        {
            { JsActiveContract: { } activeContract } =>
                (activeContract.CreatedEvent, activeContract.SynchronizerId, (string?)null, "active-contract"),
            { JsIncompleteUnassigned: { } incompleteUnassigned } =>
                (incompleteUnassigned.CreatedEvent,
                    incompleteUnassigned.UnassignedEvent?.Source,
                    incompleteUnassigned.UnassignedEvent?.Offset,
                    "incomplete-unassigned"),
            { JsIncompleteAssigned: { } incompleteAssigned } =>
                (incompleteAssigned.AssignedEvent?.CreatedEvent,
                    incompleteAssigned.AssignedEvent?.Target,
                    (string?)null,
                    "incomplete-assigned"),
            _ => ((WireCreatedEvent?)null, null, (string?)null, "empty-contract-entry"),
        };

        if (!ContractStreamProjector.TryParseOptionalOffset(unassignmentWireOffset, snapshotOffset, out var unassignmentOffset))
        {
            yield return Refused<TInterface, TView>(ContractStreamProjector.UnparseableOffsetInSnapshotRefusal(
                typeof(TInterface).Name, unassignmentWireOffset, snapshotOffset, logger));
            yield break;
        }

        var createdEvent = ClassifyActiveCreated<TInterface, TView>(
            created, wireSynchronizerId, unassignmentOffset, rawEntryKind, snapshotOffset, logger);
        yield return createdEvent;

        if (createdEvent is not InterfaceStreamEvent<TInterface, TView>.Created
            || response.ContractEntry?.JsIncompleteUnassigned?.UnassignedEvent is not { } unassigned)
        {
            yield break;
        }
        yield return ClassifyActiveUnassigned<TInterface, TView>(unassigned, unassignmentOffset, logger);
    }

    private static InterfaceStreamEvent<TInterface, TView> ClassifyActiveCreated<TInterface, TView>(
        WireCreatedEvent? created,
        string? wireSynchronizerId,
        long entryResumeOffset,
        string rawEntryKind,
        LedgerOffset? snapshotOffset,
        ILogger? logger)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        if (created is null)
        {
            return new InterfaceStreamEvent<TInterface, TView>.Unclassified(
                LedgerOffset.At(entryResumeOffset), UnclassifiedKind.Unknown, rawEntryKind);
        }
        if (!RestWireConversions.TryParseOffset(created.Offset, out var offset))
        {
            return Refused<TInterface, TView>(ContractStreamProjector.UnparseableOffsetInSnapshotRefusal(
                typeof(TInterface).Name, created.Offset, snapshotOffset, logger));
        }
        var decoded = new DecodedStreamEvent<SynchronizerId>(
            offset,
            MarkerMatcher<TInterface>.MatchesCreated(created),
            StreamEventClassifier.Synchronizer(wireSynchronizerId),
            UnclassifiedKind.CreatedEvent);
        if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
        {
            return Refused<TInterface, TView>(refusal);
        }
        try
        {
            return CreatedFromWire<TInterface, TView>(created, scope, offset);
        }
        catch (Exception decodeFailure) when (StreamEventClassifier.IsNotCancellation(decodeFailure))
        {
            return Refused<TInterface, TView>(DecodeFailure<TInterface>(offset, logger, decodeFailure));
        }
    }

    private static InterfaceStreamEvent<TInterface, TView> ClassifyActiveUnassigned<TInterface, TView>(
        WireUnassignedEvent unassigned,
        long offset,
        ILogger? logger)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        var decoded = new DecodedStreamEvent<ReassignmentScope>(
            offset,
            MatchesMarker: true,
            StreamEventClassifier.ReassignmentSynchronizers(unassigned.Source, unassigned.Target),
            UnclassifiedKind.UnassignedEvent);
        if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
        {
            return Refused<TInterface, TView>(refusal);
        }
        try
        {
            var unassignedContractId = unassigned.ContractId
                ?? throw new InvalidOperationException("Unassigned event has no contract id.");
            return new InterfaceStreamEvent<TInterface, TView>.Unassigned(
                new ContractId<TInterface>(unassignedContractId),
                LedgerOffset.At(offset),
                scope.Source,
                scope.Target,
                unassigned.ReassignmentId ?? string.Empty,
                RestWireConversions.ParseReassignmentCounter(unassigned.ReassignmentCounter),
                RestWireConversions.ToPartyList(unassigned.WitnessParties));
        }
        catch (Exception decodeFailure) when (StreamEventClassifier.IsNotCancellation(decodeFailure))
        {
            return Refused<TInterface, TView>(DecodeFailure<TInterface>(offset, logger, decodeFailure));
        }
    }

    private static InterfaceStreamEvent<TInterface, TView> CreatedFromWire<TInterface, TView>(
        WireCreatedEvent created,
        SynchronizerId synchronizerId,
        long offset)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        var contractId = created.ContractId
            ?? throw new InvalidOperationException("Created event has no contract id.");
        if (!TryResolveView<TInterface, TView>(created, out var view))
        {
            return new InterfaceStreamEvent<TInterface, TView>.Unclassified(
                LedgerOffset.At(offset), UnclassifiedKind.InterfaceViewUnavailable);
        }
        return new InterfaceStreamEvent<TInterface, TView>.Created(
            new ContractId<TInterface>(contractId),
            view,
            ContractStreamProjector.ContractKeyOf<TInterface>(created),
            LedgerOffset.At(offset),
            synchronizerId,
            RestWireConversions.ToPartyList(created.WitnessParties));
    }

    private static bool TryResolveView<TInterface, TView>(WireCreatedEvent created, out TView view)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        if (!MarkerMatcher<TInterface>.TryGetInterfaceViewRecord<TView>(created, out var record))
        {
            view = default!;
            return false;
        }

        view = TView.FromRecord(record);
        return true;
    }

    private static StreamEntryRefusal DecodeFailure<TInterface>(long offset, ILogger? logger, Exception cause)
        where TInterface : IDamlInterface =>
        StreamEventClassifier.DecodeFailure(typeof(TInterface).Name, offset, logger, cause);

    private static InterfaceStreamEvent<TInterface, TView>.Unclassified Refused<TInterface, TView>(
        StreamEntryRefusal refusal)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        new(refusal.Offset, refusal.Kind);
}
