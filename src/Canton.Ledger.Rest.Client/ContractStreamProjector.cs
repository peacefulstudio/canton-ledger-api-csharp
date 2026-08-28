// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Streams;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Canton.Ledger.Rest.Client.Raw;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WireCreatedEvent = Canton.Ledger.Rest.Client.Raw.CreatedEvent;
using WireEvent = Canton.Ledger.Rest.Client.Raw.Event;
using WireIdentifier = Canton.Ledger.Rest.Client.Raw.Identifier;
using WireReassignment = Canton.Ledger.Rest.Client.Raw.Reassignment;
using WireReassignmentEvent = Canton.Ledger.Rest.Client.Raw.ReassignmentEvent;
using WireTransaction = Canton.Ledger.Rest.Client.Raw.Transaction;
using WireUnassignedEvent = Canton.Ledger.Rest.Client.Raw.UnassignedEvent;

namespace Canton.Ledger.Rest.Client;

internal static partial class ContractStreamProjector
{
    private const string EmptyTransactionEventRawKind = "empty-event";
    private const string EmptyReassignmentEventRawKind = "empty-reassignment-event";

    public static IEnumerable<ContractStreamEvent<T>> ProjectTransactionEvents<T>(
        WireTransaction transaction,
        ILogger? logger = null)
        where T : IDamlType
    {
        if (!RestWireConversions.TryParseOffset(transaction.Offset, out var transactionOffset))
        {
            yield return UnparseableOffsetEvent<T>(transaction.Offset, logger);
            yield break;
        }

        var synchronizerId = StreamEventClassifier.Synchronizer(transaction.SynchronizerId);

        foreach (var evt in transaction.Events ?? [])
        {
            ContractStreamEvent<T> projected;
            try
            {
                projected = ProjectTransactionEvent<T>(evt, synchronizerId, transactionOffset);
            }
            catch (Exception decodeFailure) when (StreamEventClassifier.IsDecodeFailure(decodeFailure))
            {
                projected = StreamEventClassifier.DecodeFailure<T>(transactionOffset, logger, decodeFailure);
            }
            yield return projected;
        }
    }

    private static ContractStreamEvent<T> ProjectTransactionEvent<T>(
        WireEvent evt,
        SynchronizerId? synchronizerId,
        long transactionOffset)
        where T : IDamlType
    {
        if (evt?.CreatedEvent is { } created)
        {
            var offset = RestWireConversions.ParseOffset(created.Offset);
            RequireTemplateId(created.TemplateId, nameof(Raw.CreatedEvent), created.ContractId);
            var decoded = new DecodedStreamEvent<SynchronizerId>(
                offset,
                MarkerMatcher<T>.MatchesCreated(created),
                synchronizerId,
                UnclassifiedKind.CreatedEvent);
            if (!StreamEventClassifier.TryAdmit<T, SynchronizerId>(decoded, out var scope, out var unclassified))
            {
                return unclassified;
            }
            return CreatedFromWire<T>(created, scope, offset);
        }

        if (evt?.ArchivedEvent is { } archived)
        {
            var offset = RestWireConversions.ParseOffset(archived.Offset);
            RequireTemplateId(archived.TemplateId, nameof(Raw.ArchivedEvent), archived.ContractId);
            var decoded = new DecodedStreamEvent<SynchronizerId>(
                offset,
                MarkerMatcher<T>.MatchesArchived(archived),
                synchronizerId,
                UnclassifiedKind.ArchivedEvent);
            if (!StreamEventClassifier.TryAdmit<T, SynchronizerId>(decoded, out var scope, out var unclassified))
            {
                return unclassified;
            }
            var archivedContractId = archived.ContractId
                ?? throw new InvalidOperationException("Archived event has no contract id.");
            return new ContractStreamEvent<T>.Archived(
                new ContractId<T>(archivedContractId),
                LedgerOffset.At(offset),
                scope,
                RestWireConversions.ToPartyList(archived.WitnessParties));
        }

        if (evt?.ExercisedEvent is { } exercised)
        {
            var offset = RestWireConversions.ParseOffset(exercised.Offset);
            RequireTemplateId(exercised.TemplateId, nameof(Raw.ExercisedEvent), exercised.ContractId);
            var decoded = new DecodedStreamEvent<SynchronizerId>(
                offset,
                MarkerMatcher<T>.MatchesExercised(exercised),
                synchronizerId,
                UnclassifiedKind.ExercisedEvent);
            if (!StreamEventClassifier.TryAdmit<T, SynchronizerId>(decoded, out var scope, out var unclassified))
            {
                return unclassified;
            }
            var exercisedContractId = exercised.ContractId
                ?? throw new InvalidOperationException("Exercised event has no contract id.");
            var argument = exercised.ChoiceArgument is null
                ? DamlUnit.Instance
                : RestValueDecoder.ToDamlValue(exercised.ChoiceArgument);
            var result = exercised.ExerciseResult is null
                ? DamlUnit.Instance
                : RestValueDecoder.ToDamlValue(exercised.ExerciseResult);
            return new ContractStreamEvent<T>.Exercised(
                new ContractId<T>(exercisedContractId),
                exercised.Choice ?? string.Empty,
                argument,
                result,
                exercised.Consuming ?? false,
                LedgerOffset.At(offset),
                scope,
                RestWireConversions.ToPartyList(exercised.WitnessParties));
        }

        return new ContractStreamEvent<T>.Unclassified(
            LedgerOffset.At(transactionOffset), UnclassifiedKind.Unknown, EmptyTransactionEventRawKind);
    }

    public static IEnumerable<ContractStreamEvent<T>> ProjectReassignmentEvents<T>(
        WireReassignment reassignment,
        ILogger? logger = null)
        where T : IDamlType
    {
        if (!RestWireConversions.TryParseOffset(reassignment.Offset, out var reassignmentOffset))
        {
            yield return UnparseableOffsetEvent<T>(reassignment.Offset, logger);
            yield break;
        }

        foreach (var evt in reassignment.Events ?? [])
        {
            ContractStreamEvent<T> projected;
            try
            {
                projected = ProjectReassignmentEvent<T>(evt, reassignmentOffset);
            }
            catch (Exception decodeFailure) when (StreamEventClassifier.IsDecodeFailure(decodeFailure))
            {
                projected = StreamEventClassifier.DecodeFailure<T>(reassignmentOffset, logger, decodeFailure);
            }
            yield return projected;
        }
    }

    private static ContractStreamEvent<T> ProjectReassignmentEvent<T>(WireReassignmentEvent evt, long reassignmentOffset)
        where T : IDamlType
    {
        if (evt?.JsAssignmentEvent is { } assigned)
        {
            var created = assigned.CreatedEvent;
            if (created is null)
            {
                return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(reassignmentOffset), UnclassifiedKind.AssignedEvent);
            }
            var createdOffset = RestWireConversions.ParseOffset(created.Offset);
            RequireTemplateId(created.TemplateId, nameof(Raw.CreatedEvent), created.ContractId);
            var decoded = new DecodedStreamEvent<ReassignmentScope>(
                createdOffset,
                MarkerMatcher<T>.MatchesCreated(created),
                StreamEventClassifier.ReassignmentSynchronizers(assigned.Source, assigned.Target),
                UnclassifiedKind.AssignedEvent);
            if (!StreamEventClassifier.TryAdmit<T, ReassignmentScope>(decoded, out var scope, out var unclassified))
            {
                return unclassified;
            }
            var assignedContractId = created.ContractId
                ?? throw new InvalidOperationException("Assigned event's created event has no contract id.");
            if (!TryResolveCreatedPayload<T>(created, out var payload))
            {
                var unavailableViewOffset = createdOffset > 0 ? createdOffset : reassignmentOffset;
                return new ContractStreamEvent<T>.Unclassified(
                    LedgerOffset.At(unavailableViewOffset), UnclassifiedKind.InterfaceViewUnavailable);
            }
            return new ContractStreamEvent<T>.Assigned(
                new ContractId<T>(assignedContractId),
                payload,
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
            RequireTemplateId(unassigned.TemplateId, nameof(Raw.UnassignedEvent), unassigned.ContractId);
            var decoded = new DecodedStreamEvent<ReassignmentScope>(
                offset,
                MarkerMatcher<T>.MatchesUnassigned(unassigned),
                StreamEventClassifier.ReassignmentSynchronizers(unassigned.Source, unassigned.Target),
                UnclassifiedKind.UnassignedEvent);
            if (!StreamEventClassifier.TryAdmit<T, ReassignmentScope>(decoded, out var scope, out var unclassified))
            {
                return unclassified;
            }
            var unassignedContractId = unassigned.ContractId
                ?? throw new InvalidOperationException("Unassigned event has no contract id.");
            return new ContractStreamEvent<T>.Unassigned(
                new ContractId<T>(unassignedContractId),
                LedgerOffset.At(offset),
                scope.Source,
                scope.Target,
                unassigned.ReassignmentId ?? string.Empty,
                RestWireConversions.ParseReassignmentCounter(unassigned.ReassignmentCounter),
                RestWireConversions.ToPartyList(unassigned.WitnessParties));
        }

        return new ContractStreamEvent<T>.Unclassified(
            LedgerOffset.At(reassignmentOffset), UnclassifiedKind.Unknown, EmptyReassignmentEventRawKind);
    }

    public static IEnumerable<ContractStreamEvent<T>> ProjectActiveContractEntry<T>(
        GetActiveContractsResponse response,
        ILogger? logger = null,
        LedgerOffset? snapshotOffset = null)
        where T : IDamlType
    {
        var (created, wireSynchronizerId, fallbackWireOffset, rawEntryKind) = response.ContractEntry switch
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

        if (!TryParseOptionalOffset(fallbackWireOffset, snapshotOffset, out var unassignmentOffset))
        {
            yield return UnparseableOffsetInSnapshotEvent<T>(fallbackWireOffset, snapshotOffset, logger);
            yield break;
        }

        var createdEvent = ClassifyActiveCreated<T>(
            created, wireSynchronizerId, unassignmentOffset, rawEntryKind, snapshotOffset, logger);
        yield return createdEvent;

        if (createdEvent is not ContractStreamEvent<T>.Created
            || response.ContractEntry?.JsIncompleteUnassigned?.UnassignedEvent is not { } unassigned)
        {
            yield break;
        }
        yield return ClassifyActiveUnassigned<T>(unassigned, unassignmentOffset, logger);
    }

    private static ContractStreamEvent<T> ClassifyActiveCreated<T>(
        WireCreatedEvent? created,
        string? wireSynchronizerId,
        long fallbackOffset,
        string rawEntryKind,
        LedgerOffset? snapshotOffset,
        ILogger? logger)
        where T : IDamlType
    {
        if (created is null)
        {
            return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(fallbackOffset), UnclassifiedKind.Unknown, rawEntryKind);
        }
        if (!RestWireConversions.TryParseOffset(created.Offset, out var offset))
        {
            return UnparseableOffsetInSnapshotEvent<T>(created.Offset, snapshotOffset, logger);
        }
        var decoded = new DecodedStreamEvent<SynchronizerId>(
            offset,
            MarkerMatcher<T>.MatchesCreated(created),
            StreamEventClassifier.Synchronizer(wireSynchronizerId),
            UnclassifiedKind.CreatedEvent);
        if (!StreamEventClassifier.TryAdmit<T, SynchronizerId>(decoded, out var scope, out var unclassified))
        {
            return unclassified;
        }
        try
        {
            return CreatedFromWire<T>(created, scope, offset);
        }
        catch (Exception decodeFailure) when (StreamEventClassifier.IsDecodeFailure(decodeFailure))
        {
            return StreamEventClassifier.DecodeFailure<T>(offset, logger, decodeFailure);
        }
    }

    private static ContractStreamEvent<T> ClassifyActiveUnassigned<T>(
        WireUnassignedEvent unassigned,
        long offset,
        ILogger? logger)
        where T : IDamlType
    {
        var decoded = new DecodedStreamEvent<ReassignmentScope>(
            offset,
            MatchesMarker: true,
            StreamEventClassifier.ReassignmentSynchronizers(unassigned.Source, unassigned.Target),
            UnclassifiedKind.UnassignedEvent);
        if (!StreamEventClassifier.TryAdmit<T, ReassignmentScope>(decoded, out var scope, out var unclassified))
        {
            return unclassified;
        }
        try
        {
            return new ContractStreamEvent<T>.Unassigned(
                new ContractId<T>(unassigned.ContractId ?? string.Empty),
                LedgerOffset.At(offset),
                scope.Source,
                scope.Target,
                unassigned.ReassignmentId ?? string.Empty,
                RestWireConversions.ParseReassignmentCounter(unassigned.ReassignmentCounter),
                RestWireConversions.ToPartyList(unassigned.WitnessParties));
        }
        catch (Exception decodeFailure) when (StreamEventClassifier.IsDecodeFailure(decodeFailure))
        {
            return StreamEventClassifier.DecodeFailure<T>(offset, logger, decodeFailure);
        }
    }

    private static ContractStreamEvent<T> CreatedFromWire<T>(
        WireCreatedEvent created,
        SynchronizerId synchronizerId,
        long offset)
        where T : IDamlType
    {
        var contractId = created.ContractId
            ?? throw new InvalidOperationException("Created event has no contract id.");
        if (!TryResolveCreatedPayload<T>(created, out var payload))
        {
            return new ContractStreamEvent<T>.Unclassified(
                LedgerOffset.At(offset), UnclassifiedKind.InterfaceViewUnavailable);
        }
        return new ContractStreamEvent<T>.Created(
            new ContractId<T>(contractId),
            payload,
            LedgerOffset.At(offset),
            synchronizerId,
            RestWireConversions.ToPartyList(created.WitnessParties));
    }

    private static bool TryResolveCreatedPayload<T>(WireCreatedEvent created, out DamlRecord payload)
        where T : IDamlType
    {
        if (MarkerMatcher<T>.IsInterface)
        {
            return MarkerMatcher<T>.TryGetInterfaceViewRecord(created, out payload);
        }

        payload = RestValueDecoder.ToDamlRecord(created.CreateArgument);
        return true;
    }

    private static void RequireTemplateId(WireIdentifier? templateId, string wireEventKind, string? contractId)
    {
        if (templateId is null)
        {
            throw RestTransactionResultProjector.MalformedResponse(
                $"{wireEventKind} for contract '{contractId}' has no templateId");
        }
    }

    private static bool TryParseOptionalOffset(string? wireOffset, LedgerOffset? snapshotOffset, out long offset)
    {
        offset = (snapshotOffset ?? LedgerOffset.Begin).Value;
        return wireOffset is null || RestWireConversions.TryParseOffset(wireOffset, out offset);
    }

    private static ContractStreamEvent<T> UnparseableOffsetEvent<T>(string? wireOffset, ILogger? logger)
        where T : IDamlType
    {
        LogOffsetParseFailed(logger ?? NullLogger.Instance, typeof(T).Name, wireOffset);
        return new ContractStreamEvent<T>.Unclassified(LedgerOffset.Begin, UnclassifiedKind.DecodeFailure);
    }

    private static ContractStreamEvent<T> UnparseableOffsetInSnapshotEvent<T>(
        string? wireOffset,
        LedgerOffset? snapshotOffset,
        ILogger? logger)
        where T : IDamlType
    {
        if (snapshotOffset is not { } resumeOffset)
        {
            return UnparseableOffsetEvent<T>(wireOffset, logger);
        }
        LogSnapshotOffsetParseFailed(logger ?? NullLogger.Instance, typeof(T).Name, wireOffset, resumeOffset.Value);
        return new ContractStreamEvent<T>.Unclassified(resumeOffset, UnclassifiedKind.DecodeFailure);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not parse the wire offset '{WireOffset}' on the {TemplateType} stream — surfaced as Unclassified (decode-failure) carrying the begin-of-ledger offset, so a consumer resuming from this event would re-read the stream from the start")]
    private static partial void LogOffsetParseFailed(ILogger logger, string templateType, string? wireOffset);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not parse the wire offset '{WireOffset}' on the {TemplateType} active-contract snapshot — surfaced as Unclassified (decode-failure) carrying the snapshot offset {SnapshotOffset}, so a consumer resuming from this event resumes where the snapshot ended instead of re-reading the stream from the start")]
    private static partial void LogSnapshotOffsetParseFailed(ILogger logger, string templateType, string? wireOffset, long snapshotOffset);
}
