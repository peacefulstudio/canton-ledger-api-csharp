// Copyright © 2026 Peaceful Studio OÜ. All rights reserved.

using Com.Daml.Ledger.Api.V2;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client;

internal static class SubscribeRequestBuilder
{
    public static GetUpdatesRequest BuildGetUpdatesRequest(
        RuntimeCommands.SubmitterInfo submitter,
        ProtoIdentifier templateId,
        long? fromOffset)
    {
        var eventFormat = BuildTemplateEventFormat(submitter, templateId);
        return new GetUpdatesRequest
        {
            BeginExclusive = fromOffset ?? 0L,
            UpdateFormat = new UpdateFormat
            {
                IncludeTransactions = new TransactionFormat
                {
                    EventFormat = eventFormat,
                    TransactionShape = TransactionShape.LedgerEffects,
                },
            },
        };
    }

    public static GetActiveContractsRequest BuildGetActiveContractsRequest(
        RuntimeCommands.SubmitterInfo submitter,
        ProtoIdentifier templateId,
        long activeAtOffset)
    {
        return new GetActiveContractsRequest
        {
            ActiveAtOffset = activeAtOffset,
            EventFormat = BuildTemplateEventFormat(submitter, templateId),
        };
    }

    private static EventFormat BuildTemplateEventFormat(
        RuntimeCommands.SubmitterInfo submitter,
        ProtoIdentifier templateId)
    {
        var eventFormat = new EventFormat { Verbose = true };
        var filters = BuildTemplateFilters(templateId);

        AddFilterForEachParty(eventFormat, submitter.ActAs, filters);
        AddFilterForEachParty(eventFormat, submitter.ReadAs, filters);
        return eventFormat;
    }

    private static void AddFilterForEachParty(
        EventFormat eventFormat,
        IReadOnlySet<Daml.Runtime.Data.Party> parties,
        Filters filters)
    {
        foreach (var party in parties)
        {
            if (!eventFormat.FiltersByParty.ContainsKey(party.Id))
            {
                eventFormat.FiltersByParty.Add(party.Id, filters);
            }
        }
    }

    private static Filters BuildTemplateFilters(ProtoIdentifier templateId)
    {
        var filters = new Filters();
        filters.Cumulative.Add(new CumulativeFilter
        {
            TemplateFilter = new TemplateFilter
            {
                TemplateId = templateId,
            },
        });
        return filters;
    }
}
