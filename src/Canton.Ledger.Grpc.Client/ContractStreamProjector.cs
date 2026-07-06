// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Grpc;
using Daml.Runtime.Streams;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client;

internal static class ContractStreamProjector
{
    internal static class UnclassifiedKind
    {
        public const string CreatedEvent = "created-event";
        public const string ArchivedEvent = "archived-event";
        public const string ExercisedEvent = "exercised-event";
        public const string AssignedEvent = "assigned-event";
        public const string UnassignedEvent = "unassigned-event";
        public const string MissingSynchronizerId = "missing-synchronizer-id";
        public const string InterfaceViewUnavailable = "interface-view-unavailable";
    }

    public static IEnumerable<ContractStreamEvent<T>> ProjectTransactionEvents<T>(
        Transaction transaction)
        where T : IDamlType
    {
        var hasSynchronizerId = !string.IsNullOrWhiteSpace(transaction.SynchronizerId);
        var synchronizerId = hasSynchronizerId ? new SynchronizerId(transaction.SynchronizerId) : default;
        foreach (var evt in transaction.Events)
        {
            switch (evt.EventCase)
            {
                case Event.EventOneofCase.Created:
                    {
                        var created = evt.Created;
                        if (!MarkerMatcher<T>.MatchesProtoCreated(created))
                        {
                            yield return new ContractStreamEvent<T>.Unclassified(created.Offset, UnclassifiedKind.CreatedEvent);
                            break;
                        }
                        if (!hasSynchronizerId)
                        {
                            yield return new ContractStreamEvent<T>.Unclassified(created.Offset, UnclassifiedKind.MissingSynchronizerId);
                            break;
                        }
                        yield return CreatedFromProto<T>(created, synchronizerId);
                        break;
                    }
                case Event.EventOneofCase.Archived:
                    {
                        var archived = evt.Archived;
                        if (!MarkerMatcher<T>.MatchesProtoArchived(archived))
                        {
                            yield return new ContractStreamEvent<T>.Unclassified(archived.Offset, UnclassifiedKind.ArchivedEvent);
                            break;
                        }
                        if (!hasSynchronizerId)
                        {
                            yield return new ContractStreamEvent<T>.Unclassified(archived.Offset, UnclassifiedKind.MissingSynchronizerId);
                            break;
                        }
                        yield return new ContractStreamEvent<T>.Archived(
                            new ContractId<T>(archived.ContractId),
                            archived.Offset,
                            synchronizerId,
                            LedgerWireConversions.ToPartyList(archived.WitnessParties));
                        break;
                    }
                case Event.EventOneofCase.Exercised:
                    {
                        var exercised = evt.Exercised;
                        if (!MarkerMatcher<T>.MatchesProtoExercised(exercised))
                        {
                            yield return new ContractStreamEvent<T>.Unclassified(exercised.Offset, UnclassifiedKind.ExercisedEvent);
                            break;
                        }
                        if (!hasSynchronizerId)
                        {
                            yield return new ContractStreamEvent<T>.Unclassified(exercised.Offset, UnclassifiedKind.MissingSynchronizerId);
                            break;
                        }
                        var argument = exercised.ChoiceArgument is null
                            ? DamlUnit.Instance
                            : DamlValueConverter.FromProtoValue(exercised.ChoiceArgument);
                        var result = exercised.ExerciseResult is null
                            ? DamlUnit.Instance
                            : DamlValueConverter.FromProtoValue(exercised.ExerciseResult);
                        yield return new ContractStreamEvent<T>.Exercised(
                            new ContractId<T>(exercised.ContractId),
                            exercised.Choice,
                            argument,
                            result,
                            exercised.Consuming,
                            exercised.Offset,
                            synchronizerId,
                            LedgerWireConversions.ToPartyList(exercised.WitnessParties));
                        break;
                    }
                default:
                    yield return new ContractStreamEvent<T>.Unclassified(transaction.Offset, evt.EventCase.ToString());
                    break;
            }
        }
    }

    public static IEnumerable<ContractStreamEvent<T>> ProjectReassignmentEvents<T>(
        Reassignment reassignment)
        where T : IDamlType
    {
        foreach (var evt in reassignment.Events)
        {
            switch (evt.EventCase)
            {
                case ReassignmentEvent.EventOneofCase.Assigned:
                    {
                        var assigned = evt.Assigned;
                        var created = assigned.CreatedEvent;
                        if (created is null)
                        {
                            yield return new ContractStreamEvent<T>.Unclassified(reassignment.Offset, UnclassifiedKind.AssignedEvent);
                            break;
                        }
                        if (!MarkerMatcher<T>.MatchesProtoCreated(created))
                        {
                            yield return new ContractStreamEvent<T>.Unclassified(created.Offset, UnclassifiedKind.AssignedEvent);
                            break;
                        }
                        if (string.IsNullOrWhiteSpace(assigned.Source) || string.IsNullOrWhiteSpace(assigned.Target))
                        {
                            yield return new ContractStreamEvent<T>.Unclassified(created.Offset, UnclassifiedKind.MissingSynchronizerId);
                            break;
                        }
                        if (!TryResolveCreatedPayload<T>(created, out var payload))
                        {
                            yield return new ContractStreamEvent<T>.Unclassified(created.Offset, UnclassifiedKind.InterfaceViewUnavailable);
                            break;
                        }
                        yield return new ContractStreamEvent<T>.Assigned(
                            new ContractId<T>(created.ContractId),
                            payload,
                            created.Offset,
                            new SynchronizerId(assigned.Source),
                            new SynchronizerId(assigned.Target),
                            LedgerWireConversions.ToPartyList(created.WitnessParties));
                        break;
                    }
                case ReassignmentEvent.EventOneofCase.Unassigned:
                    {
                        var unassigned = evt.Unassigned;
                        if (!MarkerMatcher<T>.MatchesProtoUnassigned(unassigned))
                        {
                            yield return new ContractStreamEvent<T>.Unclassified(unassigned.Offset, UnclassifiedKind.UnassignedEvent);
                            break;
                        }
                        if (string.IsNullOrWhiteSpace(unassigned.Source) || string.IsNullOrWhiteSpace(unassigned.Target))
                        {
                            yield return new ContractStreamEvent<T>.Unclassified(unassigned.Offset, UnclassifiedKind.MissingSynchronizerId);
                            break;
                        }
                        yield return new ContractStreamEvent<T>.Unassigned(
                            new ContractId<T>(unassigned.ContractId),
                            unassigned.Offset,
                            new SynchronizerId(unassigned.Source),
                            new SynchronizerId(unassigned.Target),
                            LedgerWireConversions.ToPartyList(unassigned.WitnessParties));
                        break;
                    }
                default:
                    yield return new ContractStreamEvent<T>.Unclassified(reassignment.Offset, evt.EventCase.ToString());
                    break;
            }
        }
    }

    public static ContractStreamEvent<T> CreatedFromProto<T>(
        ProtoCreatedEvent created,
        SynchronizerId synchronizerId)
        where T : IDamlType
    {
        if (!TryResolveCreatedPayload<T>(created, out var payload))
        {
            return new ContractStreamEvent<T>.Unclassified(created.Offset, UnclassifiedKind.InterfaceViewUnavailable);
        }
        return new ContractStreamEvent<T>.Created(
            new ContractId<T>(created.ContractId),
            payload,
            created.Offset,
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

    public static bool IsTemplateMatch(ProtoIdentifier? proto, RuntimeIdentifier expected)
    {
        if (proto is null) return false;
        return string.Equals(proto.ModuleName, expected.ModuleName, StringComparison.Ordinal)
            && string.Equals(proto.EntityName, expected.EntityName, StringComparison.Ordinal);
    }

    public static ContractStreamEvent<T> ProjectActiveContractEntry<T>(GetActiveContractsResponse response)
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

        if (created is null)
        {
            return new ContractStreamEvent<T>.Unclassified(fallbackOffset, response.ContractEntryCase.ToString());
        }
        if (!MarkerMatcher<T>.MatchesProtoCreated(created))
        {
            return new ContractStreamEvent<T>.Unclassified(created.Offset, UnclassifiedKind.CreatedEvent);
        }
        if (string.IsNullOrWhiteSpace(synchronizerId))
        {
            return new ContractStreamEvent<T>.Unclassified(created.Offset, UnclassifiedKind.MissingSynchronizerId);
        }
        return CreatedFromProto<T>(created, new SynchronizerId(synchronizerId));
    }
}
