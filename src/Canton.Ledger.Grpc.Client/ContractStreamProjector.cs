// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Streams;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Grpc;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client;

internal static class ContractStreamProjector
{
    public static IEnumerable<ContractStreamEvent<T>> ProjectTransactionEvents<T>(
        Transaction transaction,
        ILogger? logger = null)
        where T : IDamlType
    {
        var synchronizerId = StreamEventClassifier.Synchronizer(transaction.SynchronizerId);
        foreach (var evt in transaction.Events)
        {
            var offset = TransactionEventOffset(evt, transaction.Offset);
            ContractStreamEvent<T> projected;
            try
            {
                projected = ProjectTransactionEvent<T>(evt, synchronizerId, transaction.Offset);
            }
            catch (Exception decodeFailure) when (StreamEventClassifier.IsDecodeFailure(decodeFailure))
            {
                projected = StreamEventClassifier.DecodeFailure<T>(offset, logger, decodeFailure);
            }
            yield return projected;
        }
    }

    private static ContractStreamEvent<T> ProjectTransactionEvent<T>(
        Event evt,
        SynchronizerId? synchronizerId,
        long transactionOffset)
        where T : IDamlType
    {
        switch (evt.EventCase)
        {
            case Event.EventOneofCase.Created:
                {
                    var created = evt.Created;
                    RequireTemplateId(created.TemplateId, nameof(Com.Daml.Ledger.Api.V2.CreatedEvent), created.ContractId);
                    var decoded = new DecodedStreamEvent<SynchronizerId>(
                        created.Offset,
                        MarkerMatcher<T>.MatchesProtoCreated(created),
                        synchronizerId,
                        UnclassifiedKind.CreatedEvent);
                    if (!StreamEventClassifier.TryAdmit<T, SynchronizerId>(decoded, out var scope, out var unclassified))
                    {
                        return unclassified;
                    }
                    return CreatedFromProto<T>(created, scope, created.Offset);
                }
            case Event.EventOneofCase.Archived:
                {
                    var archived = evt.Archived;
                    RequireTemplateId(archived.TemplateId, nameof(Com.Daml.Ledger.Api.V2.ArchivedEvent), archived.ContractId);
                    var decoded = new DecodedStreamEvent<SynchronizerId>(
                        archived.Offset,
                        MarkerMatcher<T>.MatchesProtoArchived(archived),
                        synchronizerId,
                        UnclassifiedKind.ArchivedEvent);
                    if (!StreamEventClassifier.TryAdmit<T, SynchronizerId>(decoded, out var scope, out var unclassified))
                    {
                        return unclassified;
                    }
                    return new ContractStreamEvent<T>.Archived(
                        new ContractId<T>(archived.ContractId),
                        LedgerOffset.At(archived.Offset),
                        scope,
                        LedgerWireConversions.ToPartyList(archived.WitnessParties));
                }
            case Event.EventOneofCase.Exercised:
                {
                    var exercised = evt.Exercised;
                    RequireTemplateId(exercised.TemplateId, nameof(Com.Daml.Ledger.Api.V2.ExercisedEvent), exercised.ContractId);
                    var decoded = new DecodedStreamEvent<SynchronizerId>(
                        exercised.Offset,
                        MarkerMatcher<T>.MatchesProtoExercised(exercised),
                        synchronizerId,
                        UnclassifiedKind.ExercisedEvent);
                    if (!StreamEventClassifier.TryAdmit<T, SynchronizerId>(decoded, out var scope, out var unclassified))
                    {
                        return unclassified;
                    }
                    var argument = exercised.ChoiceArgument is null
                        ? DamlUnit.Instance
                        : DamlValueConverter.FromProtoValue(exercised.ChoiceArgument);
                    var result = exercised.ExerciseResult is null
                        ? DamlUnit.Instance
                        : DamlValueConverter.FromProtoValue(exercised.ExerciseResult);
                    return new ContractStreamEvent<T>.Exercised(
                        new ContractId<T>(exercised.ContractId),
                        exercised.Choice,
                        argument,
                        result,
                        exercised.Consuming,
                        LedgerOffset.At(exercised.Offset),
                        scope,
                        LedgerWireConversions.ToPartyList(exercised.WitnessParties));
                }
            default:
                return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(transactionOffset), UnclassifiedKind.Unknown, evt.EventCase.ToString());
        }
    }

    private static long TransactionEventOffset(Event evt, long transactionOffset) => evt.EventCase switch
    {
        Event.EventOneofCase.Created => evt.Created.Offset,
        Event.EventOneofCase.Archived => evt.Archived.Offset,
        Event.EventOneofCase.Exercised => evt.Exercised.Offset,
        _ => transactionOffset,
    };

    private static void RequireTemplateId(ProtoIdentifier? templateId, string wireEventKind, string contractId)
    {
        if (templateId is null)
        {
            throw GrpcTransactionResultProjector.MalformedResponse(
                $"{wireEventKind} for contract '{contractId}' has no template_id");
        }
    }

    public static IEnumerable<ContractStreamEvent<T>> ProjectReassignmentEvents<T>(
        Reassignment reassignment,
        ILogger? logger = null)
        where T : IDamlType
    {
        foreach (var evt in reassignment.Events)
        {
            var offset = ReassignmentEventOffset(evt, reassignment.Offset);
            ContractStreamEvent<T> projected;
            try
            {
                projected = ProjectReassignmentEvent<T>(evt, reassignment.Offset);
            }
            catch (Exception decodeFailure) when (StreamEventClassifier.IsDecodeFailure(decodeFailure))
            {
                projected = StreamEventClassifier.DecodeFailure<T>(offset, logger, decodeFailure);
            }
            yield return projected;
        }
    }

    private static ContractStreamEvent<T> ProjectReassignmentEvent<T>(
        ReassignmentEvent evt,
        long reassignmentOffset)
        where T : IDamlType
    {
        switch (evt.EventCase)
        {
            case ReassignmentEvent.EventOneofCase.Assigned:
                {
                    var assigned = evt.Assigned;
                    var created = assigned.CreatedEvent;
                    if (created is null)
                    {
                        return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(reassignmentOffset), UnclassifiedKind.AssignedEvent);
                    }
                    RequireTemplateId(created.TemplateId, nameof(Com.Daml.Ledger.Api.V2.CreatedEvent), created.ContractId);
                    var decoded = new DecodedStreamEvent<ReassignmentScope>(
                        created.Offset,
                        MarkerMatcher<T>.MatchesProtoCreated(created),
                        StreamEventClassifier.ReassignmentSynchronizers(assigned.Source, assigned.Target),
                        UnclassifiedKind.AssignedEvent);
                    if (!StreamEventClassifier.TryAdmit<T, ReassignmentScope>(decoded, out var scope, out var unclassified))
                    {
                        return unclassified;
                    }
                    if (!TryResolveCreatedPayload<T>(created, out var payload))
                    {
                        var unavailableViewOffset = created.Offset > 0 ? created.Offset : reassignmentOffset;
                        return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(unavailableViewOffset), UnclassifiedKind.InterfaceViewUnavailable);
                    }
                    return new ContractStreamEvent<T>.Assigned(
                        new ContractId<T>(created.ContractId),
                        payload,
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
                    RequireTemplateId(unassigned.TemplateId, nameof(Com.Daml.Ledger.Api.V2.UnassignedEvent), unassigned.ContractId);
                    var decoded = new DecodedStreamEvent<ReassignmentScope>(
                        unassigned.Offset,
                        MarkerMatcher<T>.MatchesProtoUnassigned(unassigned),
                        StreamEventClassifier.ReassignmentSynchronizers(unassigned.Source, unassigned.Target),
                        UnclassifiedKind.UnassignedEvent);
                    if (!StreamEventClassifier.TryAdmit<T, ReassignmentScope>(decoded, out var scope, out var unclassified))
                    {
                        return unclassified;
                    }
                    return new ContractStreamEvent<T>.Unassigned(
                        new ContractId<T>(unassigned.ContractId),
                        LedgerOffset.At(unassigned.Offset),
                        scope.Source,
                        scope.Target,
                        unassigned.ReassignmentId,
                        (long)unassigned.ReassignmentCounter,
                        LedgerWireConversions.ToPartyList(unassigned.WitnessParties));
                }
            default:
                return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(reassignmentOffset), UnclassifiedKind.Unknown, evt.EventCase.ToString());
        }
    }

    private static long ReassignmentEventOffset(ReassignmentEvent evt, long reassignmentOffset) => evt.EventCase switch
    {
        ReassignmentEvent.EventOneofCase.Assigned => evt.Assigned.CreatedEvent?.Offset ?? reassignmentOffset,
        ReassignmentEvent.EventOneofCase.Unassigned => evt.Unassigned.Offset,
        _ => reassignmentOffset,
    };

    public static ContractStreamEvent<T> CreatedFromProto<T>(
        ProtoCreatedEvent created,
        SynchronizerId synchronizerId,
        long unavailableViewOffset)
        where T : IDamlType
    {
        if (!TryResolveCreatedPayload<T>(created, out var payload))
        {
            return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(unavailableViewOffset), UnclassifiedKind.InterfaceViewUnavailable);
        }
        return new ContractStreamEvent<T>.Created(
            new ContractId<T>(created.ContractId),
            payload,
            LedgerOffset.At(created.Offset),
            synchronizerId,
            LedgerWireConversions.ToPartyList(created.WitnessParties));
    }

    private static bool TryResolveCreatedPayload<T>(ProtoCreatedEvent created, out DamlRecord payload)
        where T : IDamlType
    {
        if (MarkerMatcher<T>.IsInterface)
        {
            return MarkerMatcher<T>.TryGetInterfaceViewRecord(created, out payload);
        }

        payload = created.CreateArguments is null
            ? new DamlRecord(null, [])
            : DamlValueConverter.FromProtoRecord(created.CreateArguments);
        return true;
    }

    public static bool IsTemplateMatch(ProtoIdentifier? proto, RuntimeIdentifier expected) =>
        proto is not null && MatchesModuleEntity(proto.ModuleName, proto.EntityName, expected);

    internal static bool MatchesModuleEntity(string moduleName, string entityName, RuntimeIdentifier expected) =>
        string.Equals(moduleName, expected.ModuleName, StringComparison.Ordinal)
        && string.Equals(entityName, expected.EntityName, StringComparison.Ordinal);

    public static IEnumerable<ContractStreamEvent<T>> ProjectActiveContractEntry<T>(
        GetActiveContractsResponse response,
        ILogger? logger = null,
        LedgerOffset? snapshotOffset = null)
        where T : IDamlType
    {
        var snapshotFallbackOffset = (snapshotOffset ?? LedgerOffset.Begin).Value;
        var (created, synchronizerId, fallbackOffset) = response.ContractEntryCase switch
        {
            GetActiveContractsResponse.ContractEntryOneofCase.ActiveContract
                => (response.ActiveContract?.CreatedEvent, response.ActiveContract?.SynchronizerId, snapshotFallbackOffset),
            GetActiveContractsResponse.ContractEntryOneofCase.IncompleteUnassigned
                => (response.IncompleteUnassigned?.CreatedEvent, response.IncompleteUnassigned?.UnassignedEvent?.Source, UnassignmentOffsetOr(response.IncompleteUnassigned, snapshotFallbackOffset)),
            GetActiveContractsResponse.ContractEntryOneofCase.IncompleteAssigned
                => (response.IncompleteAssigned?.AssignedEvent?.CreatedEvent, response.IncompleteAssigned?.AssignedEvent?.Target, snapshotFallbackOffset),
            _ => (null, null, snapshotFallbackOffset),
        };

        var createdEvent = ClassifyActiveCreated<T>(response.ContractEntryCase, created, synchronizerId, fallbackOffset, logger);
        yield return createdEvent;

        var createdEventMatchedMarker = createdEvent is ContractStreamEvent<T>.Created;
        if (response.ContractEntryCase == GetActiveContractsResponse.ContractEntryOneofCase.IncompleteUnassigned
            && createdEventMatchedMarker
            && response.IncompleteUnassigned?.UnassignedEvent is { } unassigned)
        {
            var unassignmentOffset = UnassignedOffsetOr(unassigned, snapshotFallbackOffset);
            var decoded = new DecodedStreamEvent<ReassignmentScope>(
                unassignmentOffset,
                MatchesMarker: createdEventMatchedMarker,
                StreamEventClassifier.ReassignmentSynchronizers(unassigned.Source, unassigned.Target),
                UnclassifiedKind.UnassignedEvent);
            if (!StreamEventClassifier.TryAdmit<T, ReassignmentScope>(decoded, out var scope, out var unclassified))
            {
                yield return unclassified;
                yield break;
            }
            yield return new ContractStreamEvent<T>.Unassigned(
                new ContractId<T>(unassigned.ContractId),
                LedgerOffset.At(unassignmentOffset),
                scope.Source,
                scope.Target,
                unassigned.ReassignmentId,
                (long)unassigned.ReassignmentCounter,
                LedgerWireConversions.ToPartyList(unassigned.WitnessParties));
        }
    }

    private static ContractStreamEvent<T> ClassifyActiveCreated<T>(
        GetActiveContractsResponse.ContractEntryOneofCase entryCase,
        ProtoCreatedEvent? created,
        string? wireSynchronizerId,
        long fallbackOffset,
        ILogger? logger)
        where T : IDamlType
    {
        if (created is null)
        {
            return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(fallbackOffset), UnclassifiedKind.Unknown, entryCase.ToString());
        }
        var resumeOffset = created.Offset > 0 ? created.Offset : fallbackOffset;
        var decoded = new DecodedStreamEvent<SynchronizerId>(
            resumeOffset,
            MarkerMatcher<T>.MatchesProtoCreated(created),
            StreamEventClassifier.Synchronizer(wireSynchronizerId),
            UnclassifiedKind.CreatedEvent);
        if (!StreamEventClassifier.TryAdmit<T, SynchronizerId>(decoded, out var scope, out var unclassified))
        {
            return unclassified;
        }
        try
        {
            return CreatedFromProto<T>(created, scope, resumeOffset);
        }
        catch (Exception decodeFailure) when (StreamEventClassifier.IsDecodeFailure(decodeFailure))
        {
            return StreamEventClassifier.DecodeFailure<T>(resumeOffset, logger, decodeFailure);
        }
    }

    private static long UnassignmentOffsetOr(IncompleteUnassigned? entry, long fallbackOffset) =>
        entry?.UnassignedEvent is { } unassigned ? UnassignedOffsetOr(unassigned, fallbackOffset) : fallbackOffset;

    private static long UnassignedOffsetOr(UnassignedEvent unassigned, long fallbackOffset) =>
        unassigned.Offset > 0 ? unassigned.Offset : fallbackOffset;
}
