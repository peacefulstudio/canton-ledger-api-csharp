// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Streams;
using Canton.Ledger.Kernel.Wire;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Grpc;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;

namespace Canton.Ledger.Grpc.Client;

internal static class GrpcInterfaceStreamProjector
{
    public static IEnumerable<InterfaceStreamEvent<TInterface, TView>> ProjectTransactionEvents<TInterface, TView>(
        Transaction transaction,
        ILogger? logger = null)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        var synchronizerId = StreamEventClassifier.Synchronizer(transaction.SynchronizerId);
        foreach (var evt in transaction.Events)
        {
            InterfaceStreamEvent<TInterface, TView> projected;
            try
            {
                projected = ProjectTransactionEvent<TInterface, TView>(evt, synchronizerId, transaction.Offset);
            }
            catch (Exception decodeFailure) when (StreamEventClassifier.IsNotCancellation(decodeFailure))
            {
                projected = Refused<TInterface, TView>(
                    DecodeFailure<TInterface>(transaction.Offset, logger, decodeFailure));
            }
            yield return projected;
        }
    }

    private static InterfaceStreamEvent<TInterface, TView> ProjectTransactionEvent<TInterface, TView>(
        Event evt,
        SynchronizerId? synchronizerId,
        long transactionOffset)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        switch (evt.EventCase)
        {
            case Event.EventOneofCase.Created:
                {
                    var created = evt.Created;
                    GrpcContractStreamProjector.RequireTemplateId(
                        created.TemplateId, nameof(Com.Daml.Ledger.Api.V2.CreatedEvent), created.ContractId);
                    var decoded = new DecodedStreamEvent<SynchronizerId>(
                        created.Offset,
                        GrpcMarkerMatcher<TInterface>.MatchesProtoCreated(created),
                        synchronizerId,
                        UnclassifiedKind.CreatedEvent);
                    if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
                    {
                        return Refused<TInterface, TView>(refusal);
                    }
                    return CreatedFromProto<TInterface, TView>(created, scope, created.Offset);
                }
            case Event.EventOneofCase.Archived:
                {
                    var archived = evt.Archived;
                    GrpcContractStreamProjector.RequireTemplateId(
                        archived.TemplateId, nameof(Com.Daml.Ledger.Api.V2.ArchivedEvent), archived.ContractId);
                    var decoded = new DecodedStreamEvent<SynchronizerId>(
                        archived.Offset,
                        GrpcMarkerMatcher<TInterface>.MatchesProtoArchived(archived),
                        synchronizerId,
                        UnclassifiedKind.ArchivedEvent);
                    if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
                    {
                        return Refused<TInterface, TView>(refusal);
                    }
                    return new InterfaceStreamEvent<TInterface, TView>.Archived(
                        new ContractId<TInterface>(archived.ContractId),
                        LedgerOffset.At(archived.Offset),
                        scope,
                        LedgerWireConversions.ToPartyList(archived.WitnessParties));
                }
            case Event.EventOneofCase.Exercised:
                {
                    var exercised = evt.Exercised;
                    GrpcContractStreamProjector.RequireTemplateId(
                        exercised.TemplateId, nameof(Com.Daml.Ledger.Api.V2.ExercisedEvent), exercised.ContractId);
                    var decoded = new DecodedStreamEvent<SynchronizerId>(
                        exercised.Offset,
                        GrpcMarkerMatcher<TInterface>.MatchesProtoExercised(exercised),
                        synchronizerId,
                        UnclassifiedKind.ExercisedEvent);
                    if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
                    {
                        return Refused<TInterface, TView>(refusal);
                    }
                    var argument = DamlValueConverter.FromProtoValue(
                        GrpcTransactionResultProjector.RequireChoiceArgument(exercised));
                    var result = DamlValueConverter.FromProtoValue(
                        GrpcTransactionResultProjector.RequireExerciseResult(exercised));
                    return new InterfaceStreamEvent<TInterface, TView>.Exercised(
                        new ContractId<TInterface>(exercised.ContractId),
                        exercised.Choice,
                        argument,
                        result,
                        exercised.Consuming,
                        LedgerOffset.At(exercised.Offset),
                        scope,
                        LedgerWireConversions.ToPartyList(exercised.WitnessParties));
                }
            default:
                return new InterfaceStreamEvent<TInterface, TView>.Unclassified(
                    LedgerOffset.At(transactionOffset), UnclassifiedKind.Unknown, evt.EventCase.ToString());
        }
    }

    public static IEnumerable<InterfaceStreamEvent<TInterface, TView>> ProjectReassignmentEvents<TInterface, TView>(
        Reassignment reassignment,
        ILogger? logger = null)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        foreach (var evt in reassignment.Events)
        {
            InterfaceStreamEvent<TInterface, TView> projected;
            try
            {
                projected = ProjectReassignmentEvent<TInterface, TView>(evt, reassignment.Offset);
            }
            catch (Exception decodeFailure) when (StreamEventClassifier.IsNotCancellation(decodeFailure))
            {
                projected = Refused<TInterface, TView>(
                    DecodeFailure<TInterface>(reassignment.Offset, logger, decodeFailure));
            }
            yield return projected;
        }
    }

    private static InterfaceStreamEvent<TInterface, TView> ProjectReassignmentEvent<TInterface, TView>(
        ReassignmentEvent evt,
        long reassignmentOffset)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        switch (evt.EventCase)
        {
            case ReassignmentEvent.EventOneofCase.Assigned:
                {
                    var assigned = evt.Assigned;
                    var created = assigned.CreatedEvent;
                    if (created is null)
                    {
                        return new InterfaceStreamEvent<TInterface, TView>.Unclassified(
                            LedgerOffset.At(reassignmentOffset), UnclassifiedKind.AssignedEvent);
                    }
                    GrpcContractStreamProjector.RequireTemplateId(
                        created.TemplateId, nameof(Com.Daml.Ledger.Api.V2.CreatedEvent), created.ContractId);
                    var decoded = new DecodedStreamEvent<ReassignmentScope>(
                        created.Offset,
                        GrpcMarkerMatcher<TInterface>.MatchesProtoCreated(created),
                        StreamEventClassifier.ReassignmentSynchronizers(assigned.Source, assigned.Target),
                        UnclassifiedKind.AssignedEvent);
                    if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
                    {
                        return Refused<TInterface, TView>(refusal);
                    }
                    if (!TryResolveView<TInterface, TView>(created, out var view))
                    {
                        var unavailableViewOffset = created.Offset > 0 ? created.Offset : reassignmentOffset;
                        return new InterfaceStreamEvent<TInterface, TView>.Unclassified(
                            LedgerOffset.At(unavailableViewOffset), UnclassifiedKind.InterfaceViewUnavailable);
                    }
                    return new InterfaceStreamEvent<TInterface, TView>.Assigned(
                        new ContractId<TInterface>(created.ContractId),
                        view,
                        GrpcContractStreamProjector.ContractKeyOf(created),
                        LedgerOffset.At(created.Offset),
                        scope.Source,
                        scope.Target,
                        assigned.ReassignmentId,
                        (long)assigned.ReassignmentCounter,
                        LedgerWireConversions.ToPartyList(created.WitnessParties));
                }
            case ReassignmentEvent.EventOneofCase.Unassigned:
                {
                    var unassigned = evt.Unassigned;
                    GrpcContractStreamProjector.RequireTemplateId(
                        unassigned.TemplateId, nameof(Com.Daml.Ledger.Api.V2.UnassignedEvent), unassigned.ContractId);
                    var decoded = new DecodedStreamEvent<ReassignmentScope>(
                        unassigned.Offset,
                        GrpcMarkerMatcher<TInterface>.MatchesProtoUnassigned(unassigned),
                        StreamEventClassifier.ReassignmentSynchronizers(unassigned.Source, unassigned.Target),
                        UnclassifiedKind.UnassignedEvent);
                    if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
                    {
                        return Refused<TInterface, TView>(refusal);
                    }
                    return new InterfaceStreamEvent<TInterface, TView>.Unassigned(
                        new ContractId<TInterface>(unassigned.ContractId),
                        LedgerOffset.At(unassigned.Offset),
                        scope.Source,
                        scope.Target,
                        unassigned.ReassignmentId,
                        (long)unassigned.ReassignmentCounter,
                        LedgerWireConversions.ToPartyList(unassigned.WitnessParties));
                }
            default:
                return new InterfaceStreamEvent<TInterface, TView>.Unclassified(
                    LedgerOffset.At(reassignmentOffset), UnclassifiedKind.Unknown, evt.EventCase.ToString());
        }
    }

    public static IEnumerable<InterfaceStreamEvent<TInterface, TView>> ProjectActiveContractEntry<TInterface, TView>(
        GetActiveContractsResponse response,
        ILogger? logger = null,
        LedgerOffset? snapshotOffset = null)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        var snapshotResumeOffset = (snapshotOffset ?? LedgerOffset.Begin).Value;
        var (created, synchronizerId, entryResumeOffset) = response.ContractEntryCase switch
        {
            GetActiveContractsResponse.ContractEntryOneofCase.ActiveContract
                => (response.ActiveContract?.CreatedEvent, response.ActiveContract?.SynchronizerId, snapshotResumeOffset),
            GetActiveContractsResponse.ContractEntryOneofCase.IncompleteUnassigned
                => (response.IncompleteUnassigned?.CreatedEvent,
                    response.IncompleteUnassigned?.UnassignedEvent?.Source,
                    GrpcContractStreamProjector.UnassignmentOffsetOr(response.IncompleteUnassigned, snapshotResumeOffset)),
            GetActiveContractsResponse.ContractEntryOneofCase.IncompleteAssigned
                => (response.IncompleteAssigned?.AssignedEvent?.CreatedEvent,
                    response.IncompleteAssigned?.AssignedEvent?.Target,
                    snapshotResumeOffset),
            _ => (null, null, snapshotResumeOffset),
        };

        var createdEvent = ClassifyActiveCreated<TInterface, TView>(
            response.ContractEntryCase, created, synchronizerId, entryResumeOffset, logger);
        yield return createdEvent;

        if (createdEvent is not InterfaceStreamEvent<TInterface, TView>.Created
            || response.ContractEntryCase != GetActiveContractsResponse.ContractEntryOneofCase.IncompleteUnassigned
            || response.IncompleteUnassigned?.UnassignedEvent is not { } unassigned)
        {
            yield break;
        }
        yield return ClassifyActiveUnassigned<TInterface, TView>(
            unassigned, GrpcContractStreamProjector.UnassignedOffsetOr(unassigned, snapshotResumeOffset), logger);
    }

    private static InterfaceStreamEvent<TInterface, TView> ClassifyActiveCreated<TInterface, TView>(
        GetActiveContractsResponse.ContractEntryOneofCase entryCase,
        ProtoCreatedEvent? created,
        string? wireSynchronizerId,
        long entryResumeOffset,
        ILogger? logger)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        if (created is null)
        {
            return new InterfaceStreamEvent<TInterface, TView>.Unclassified(
                LedgerOffset.At(entryResumeOffset), UnclassifiedKind.Unknown, entryCase.ToString());
        }
        var resumeOffset = created.Offset > 0 ? created.Offset : entryResumeOffset;
        var decoded = new DecodedStreamEvent<SynchronizerId>(
            resumeOffset,
            GrpcMarkerMatcher<TInterface>.MatchesProtoCreated(created),
            StreamEventClassifier.Synchronizer(wireSynchronizerId),
            UnclassifiedKind.CreatedEvent);
        if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
        {
            return Refused<TInterface, TView>(refusal);
        }
        try
        {
            return CreatedFromProto<TInterface, TView>(created, scope, resumeOffset);
        }
        catch (Exception decodeFailure) when (StreamEventClassifier.IsNotCancellation(decodeFailure))
        {
            return Refused<TInterface, TView>(DecodeFailure<TInterface>(resumeOffset, logger, decodeFailure));
        }
    }

    private static InterfaceStreamEvent<TInterface, TView> ClassifyActiveUnassigned<TInterface, TView>(
        UnassignedEvent unassigned,
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
            return new InterfaceStreamEvent<TInterface, TView>.Unassigned(
                new ContractId<TInterface>(unassigned.ContractId),
                LedgerOffset.At(offset),
                scope.Source,
                scope.Target,
                unassigned.ReassignmentId,
                (long)unassigned.ReassignmentCounter,
                LedgerWireConversions.ToPartyList(unassigned.WitnessParties));
        }
        catch (Exception decodeFailure) when (StreamEventClassifier.IsNotCancellation(decodeFailure))
        {
            return Refused<TInterface, TView>(DecodeFailure<TInterface>(offset, logger, decodeFailure));
        }
    }

    private static InterfaceStreamEvent<TInterface, TView> CreatedFromProto<TInterface, TView>(
        ProtoCreatedEvent created,
        SynchronizerId synchronizerId,
        long unavailableViewOffset)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        if (!TryResolveView<TInterface, TView>(created, out var view))
        {
            return new InterfaceStreamEvent<TInterface, TView>.Unclassified(
                LedgerOffset.At(unavailableViewOffset), UnclassifiedKind.InterfaceViewUnavailable);
        }
        return new InterfaceStreamEvent<TInterface, TView>.Created(
            new ContractId<TInterface>(created.ContractId),
            view,
            GrpcContractStreamProjector.ContractKeyOf(created),
            LedgerOffset.At(created.Offset),
            synchronizerId,
            LedgerWireConversions.ToPartyList(created.WitnessParties));
    }

    private static bool TryResolveView<TInterface, TView>(ProtoCreatedEvent created, out TView view)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        if (!GrpcMarkerMatcher<TInterface>.TryGetInterfaceViewRecord(created, out var record))
        {
            view = default!;
            return false;
        }

        _ = created.CreateArguments
            ?? throw MalformedResponse.MissingRequiredField(
                $"CreatedEvent for contract '{created.ContractId}' has no create_arguments");
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
