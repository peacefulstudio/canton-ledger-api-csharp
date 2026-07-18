// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Grpc;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client;

internal static partial class ContractStreamProjector
{
    public static IEnumerable<ContractStreamEvent<T>> ProjectTransactionEvents<T>(
        Transaction transaction,
        ILogger? logger = null)
        where T : IDamlType
    {
        var hasSynchronizerId = !string.IsNullOrWhiteSpace(transaction.SynchronizerId);
        var synchronizerId = hasSynchronizerId ? new SynchronizerId(transaction.SynchronizerId) : default;
        foreach (var evt in transaction.Events)
        {
            var offset = TransactionEventOffset(evt, transaction.Offset);
            ContractStreamEvent<T> projected;
            try
            {
                projected = ProjectTransactionEvent<T>(evt, hasSynchronizerId, synchronizerId, transaction.Offset);
            }
            catch (Exception decodeFailure) when (decodeFailure is not OperationCanceledException)
            {
                projected = DecodeFailureEvent<T>(offset, logger, decodeFailure);
            }
            yield return projected;
        }
    }

    private static ContractStreamEvent<T> ProjectTransactionEvent<T>(
        Event evt,
        bool hasSynchronizerId,
        SynchronizerId synchronizerId,
        long transactionOffset)
        where T : IDamlType
    {
        switch (evt.EventCase)
        {
            case Event.EventOneofCase.Created:
                {
                    var created = evt.Created;
                    if (!MarkerMatcher<T>.MatchesProtoCreated(created))
                    {
                        return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(created.Offset), UnclassifiedKind.CreatedEvent);
                    }
                    if (!hasSynchronizerId)
                    {
                        return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(created.Offset), UnclassifiedKind.MissingSynchronizerId);
                    }
                    return CreatedFromProto<T>(created, synchronizerId);
                }
            case Event.EventOneofCase.Archived:
                {
                    var archived = evt.Archived;
                    if (!MarkerMatcher<T>.MatchesProtoArchived(archived))
                    {
                        return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(archived.Offset), UnclassifiedKind.ArchivedEvent);
                    }
                    if (!hasSynchronizerId)
                    {
                        return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(archived.Offset), UnclassifiedKind.MissingSynchronizerId);
                    }
                    return new ContractStreamEvent<T>.Archived(
                        new ContractId<T>(archived.ContractId),
                        LedgerOffset.At(archived.Offset),
                        synchronizerId,
                        LedgerWireConversions.ToPartyList(archived.WitnessParties));
                }
            case Event.EventOneofCase.Exercised:
                {
                    var exercised = evt.Exercised;
                    if (!MarkerMatcher<T>.MatchesProtoExercised(exercised))
                    {
                        return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(exercised.Offset), UnclassifiedKind.ExercisedEvent);
                    }
                    if (!hasSynchronizerId)
                    {
                        return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(exercised.Offset), UnclassifiedKind.MissingSynchronizerId);
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
                        synchronizerId,
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

    private static ContractStreamEvent<T> DecodeFailureEvent<T>(long offset, ILogger? logger, Exception decodeFailure)
        where T : IDamlType
    {
        LogEventDecodeFailed(logger ?? NullLogger.Instance, typeof(T).Name, offset, decodeFailure);
        return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(offset), UnclassifiedKind.DecodeFailure);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not decode event at offset {Offset} on the {TemplateType} stream — surfaced as Unclassified (decode-failure)")]
    private static partial void LogEventDecodeFailed(ILogger logger, string templateType, long offset, Exception exception);

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
            catch (Exception decodeFailure) when (decodeFailure is not OperationCanceledException)
            {
                projected = DecodeFailureEvent<T>(offset, logger, decodeFailure);
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
                    if (!MarkerMatcher<T>.MatchesProtoCreated(created))
                    {
                        return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(created.Offset), UnclassifiedKind.AssignedEvent);
                    }
                    if (string.IsNullOrWhiteSpace(assigned.Source) || string.IsNullOrWhiteSpace(assigned.Target))
                    {
                        return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(created.Offset), UnclassifiedKind.MissingSynchronizerId);
                    }
                    if (!TryResolveCreatedPayload<T>(created, out var payload))
                    {
                        return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(created.Offset), UnclassifiedKind.InterfaceViewUnavailable);
                    }
                    return new ContractStreamEvent<T>.Assigned(
                        new ContractId<T>(created.ContractId),
                        payload,
                        LedgerOffset.At(created.Offset),
                        new SynchronizerId(assigned.Source),
                        new SynchronizerId(assigned.Target),
                        assigned.ReassignmentId,
                        (long)assigned.ReassignmentCounter,
                        LedgerWireConversions.ToPartyList(created.WitnessParties));
                }
            case ReassignmentEvent.EventOneofCase.Unassigned:
                {
                    var unassigned = evt.Unassigned;
                    if (!MarkerMatcher<T>.MatchesProtoUnassigned(unassigned))
                    {
                        return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(unassigned.Offset), UnclassifiedKind.UnassignedEvent);
                    }
                    if (string.IsNullOrWhiteSpace(unassigned.Source) || string.IsNullOrWhiteSpace(unassigned.Target))
                    {
                        return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(unassigned.Offset), UnclassifiedKind.MissingSynchronizerId);
                    }
                    return new ContractStreamEvent<T>.Unassigned(
                        new ContractId<T>(unassigned.ContractId),
                        LedgerOffset.At(unassigned.Offset),
                        new SynchronizerId(unassigned.Source),
                        new SynchronizerId(unassigned.Target),
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
        SynchronizerId synchronizerId)
        where T : IDamlType
    {
        if (!TryResolveCreatedPayload<T>(created, out var payload))
        {
            return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(created.Offset), UnclassifiedKind.InterfaceViewUnavailable);
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
        ILogger? logger = null)
        where T : IDamlType
    {
        var (created, synchronizerId, fallbackOffset) = response.ContractEntryCase switch
        {
            GetActiveContractsResponse.ContractEntryOneofCase.ActiveContract
                => (response.ActiveContract?.CreatedEvent, response.ActiveContract?.SynchronizerId, 0L),
            GetActiveContractsResponse.ContractEntryOneofCase.IncompleteUnassigned
                => (response.IncompleteUnassigned?.CreatedEvent, response.IncompleteUnassigned?.UnassignedEvent?.Source, response.IncompleteUnassigned?.UnassignedEvent?.Offset ?? 0L),
            GetActiveContractsResponse.ContractEntryOneofCase.IncompleteAssigned
                => (response.IncompleteAssigned?.AssignedEvent?.CreatedEvent, response.IncompleteAssigned?.AssignedEvent?.Target, 0L),
            _ => (null, null, 0L),
        };

        var createdEvent = ClassifyActiveCreated<T>(response.ContractEntryCase, created, synchronizerId, fallbackOffset, logger);
        yield return createdEvent;

        if (response.ContractEntryCase == GetActiveContractsResponse.ContractEntryOneofCase.IncompleteUnassigned
            && createdEvent is ContractStreamEvent<T>.Created
            && response.IncompleteUnassigned?.UnassignedEvent is { } unassigned)
        {
            if (string.IsNullOrWhiteSpace(unassigned.Source) || string.IsNullOrWhiteSpace(unassigned.Target))
            {
                yield return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(unassigned.Offset), UnclassifiedKind.MissingSynchronizerId);
                yield break;
            }
            yield return new ContractStreamEvent<T>.Unassigned(
                new ContractId<T>(unassigned.ContractId),
                LedgerOffset.At(unassigned.Offset),
                new SynchronizerId(unassigned.Source),
                new SynchronizerId(unassigned.Target),
                unassigned.ReassignmentId,
                (long)unassigned.ReassignmentCounter,
                LedgerWireConversions.ToPartyList(unassigned.WitnessParties));
        }
    }

    private static ContractStreamEvent<T> ClassifyActiveCreated<T>(
        GetActiveContractsResponse.ContractEntryOneofCase entryCase,
        ProtoCreatedEvent? created,
        string? synchronizerId,
        long fallbackOffset,
        ILogger? logger)
        where T : IDamlType
    {
        if (created is null)
        {
            return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(fallbackOffset), UnclassifiedKind.Unknown, entryCase.ToString());
        }
        if (!MarkerMatcher<T>.MatchesProtoCreated(created))
        {
            return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(created.Offset), UnclassifiedKind.CreatedEvent);
        }
        if (string.IsNullOrWhiteSpace(synchronizerId))
        {
            return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(created.Offset), UnclassifiedKind.MissingSynchronizerId);
        }
        try
        {
            return CreatedFromProto<T>(created, new SynchronizerId(synchronizerId));
        }
        catch (Exception decodeFailure) when (decodeFailure is not OperationCanceledException)
        {
            return DecodeFailureEvent<T>(created.Offset, logger, decodeFailure);
        }
    }
}
