// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Com.Daml.Ledger.Api.V2;
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
    public static IEnumerable<ContractStreamEvent<T>> ProjectTransactionEvents<T>(
        Transaction transaction,
        RuntimeIdentifier templateId)
        where T : ITemplate
    {
        foreach (var evt in transaction.Events)
        {
            switch (evt.EventCase)
            {
                case Event.EventOneofCase.Created:
                    {
                        var created = evt.Created;
                        if (!IsTemplateMatch(created.TemplateId, templateId)) continue;
                        yield return CreatedFromProto<T>(created);
                        break;
                    }
                case Event.EventOneofCase.Archived:
                    {
                        var archived = evt.Archived;
                        if (!IsTemplateMatch(archived.TemplateId, templateId)) continue;
                        yield return new ContractStreamEvent<T>.Archived(
                            new ContractId<T>(archived.ContractId),
                            archived.Offset,
                            LedgerWireConversions.ToPartyList(archived.WitnessParties));
                        break;
                    }
                case Event.EventOneofCase.Exercised:
                    {
                        var exercised = evt.Exercised;
                        if (!IsTemplateMatch(exercised.TemplateId, templateId)) continue;
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
                            LedgerWireConversions.ToPartyList(exercised.WitnessParties));
                        break;
                    }
            }
        }
    }

    public static IEnumerable<ContractStreamEvent<T>> ProjectReassignmentEvents<T>(
        Reassignment reassignment,
        RuntimeIdentifier templateId)
        where T : ITemplate
    {
        foreach (var evt in reassignment.Events)
        {
            switch (evt.EventCase)
            {
                case ReassignmentEvent.EventOneofCase.Assigned:
                    {
                        var assigned = evt.Assigned;
                        var created = assigned.CreatedEvent;
                        if (created is null) continue;
                        if (!IsTemplateMatch(created.TemplateId, templateId)) continue;
                        var payload = created.CreateArguments is null
                            ? new DamlRecord(null, [])
                            : DamlValueConverter.FromProtoRecord(created.CreateArguments);
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
                        if (!IsTemplateMatch(unassigned.TemplateId, templateId)) continue;
                        yield return new ContractStreamEvent<T>.Unassigned(
                            new ContractId<T>(unassigned.ContractId),
                            unassigned.Offset,
                            new SynchronizerId(unassigned.Source),
                            new SynchronizerId(unassigned.Target),
                            LedgerWireConversions.ToPartyList(unassigned.WitnessParties));
                        break;
                    }
            }
        }
    }

    public static ContractStreamEvent<T>.Created CreatedFromProto<T>(ProtoCreatedEvent created)
        where T : ITemplate
    {
        var payload = created.CreateArguments is null
            ? new DamlRecord(null, [])
            : DamlValueConverter.FromProtoRecord(created.CreateArguments);
        return new ContractStreamEvent<T>.Created(
            new ContractId<T>(created.ContractId),
            payload,
            created.Offset,
            LedgerWireConversions.ToPartyList(created.WitnessParties));
    }

    public static bool IsTemplateMatch(ProtoIdentifier? proto, RuntimeIdentifier expected)
    {
        if (proto is null) return false;
        return string.Equals(proto.ModuleName, expected.ModuleName, StringComparison.Ordinal)
            && string.Equals(proto.EntityName, expected.EntityName, StringComparison.Ordinal);
    }

    public static ProtoCreatedEvent? ExtractCreatedEvent(GetActiveContractsResponse response) =>
        response.ContractEntryCase switch
        {
            GetActiveContractsResponse.ContractEntryOneofCase.ActiveContract
                => response.ActiveContract?.CreatedEvent,
            GetActiveContractsResponse.ContractEntryOneofCase.IncompleteUnassigned
                => response.IncompleteUnassigned?.CreatedEvent,
            GetActiveContractsResponse.ContractEntryOneofCase.IncompleteAssigned
                => response.IncompleteAssigned?.AssignedEvent?.CreatedEvent,
            _ => null,
        };
}
