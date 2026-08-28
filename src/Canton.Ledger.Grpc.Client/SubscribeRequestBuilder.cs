// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Streams;
using Com.Daml.Ledger.Api.V2;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client;

internal static class SubscribeRequestBuilder
{
    public static GetUpdatesRequest BuildGetUpdatesRequest(
        RuntimeCommands.SubmitterInfo submitter,
        ProtoIdentifier filterId,
        long? fromOffset,
        long? toOffset,
        bool isInterface = false,
        TransactionShape transactionShape = TransactionShape.AcsDelta)
    {
        var request = new GetUpdatesRequest
        {
            BeginExclusive = fromOffset ?? 0L,
            UpdateFormat = new UpdateFormat
            {
                IncludeTransactions = new TransactionFormat
                {
                    EventFormat = BuildEventFormat(submitter, filterId, isInterface),
                    TransactionShape = transactionShape,
                },
                IncludeReassignments = BuildEventFormat(submitter, filterId, isInterface),
            },
        };

        if (toOffset is { } endInclusive)
        {
            request.EndInclusive = endInclusive;
        }

        return request;
    }

    public static UpdateFormat BuildTransactionUpdateFormat(RuntimeCommands.SubmitterInfo submitter) =>
        new UpdateFormat
        {
            IncludeTransactions = BuildTransactionFormat(submitter),
        };

    public static TransactionFormat BuildTransactionFormat(RuntimeCommands.SubmitterInfo submitter)
    {
        var eventFormat = new EventFormat { Verbose = true };

        AddFilterForEachParty(eventFormat, submitter, static () => new Filters());

        return new TransactionFormat
        {
            EventFormat = eventFormat,
            TransactionShape = TransactionShape.LedgerEffects,
        };
    }

    public static GetActiveContractsRequest BuildGetActiveContractsRequest(
        RuntimeCommands.SubmitterInfo submitter,
        ProtoIdentifier filterId,
        long activeAtOffset,
        bool isInterface = false)
    {
        return new GetActiveContractsRequest
        {
            ActiveAtOffset = activeAtOffset,
            EventFormat = BuildEventFormat(submitter, filterId, isInterface),
        };
    }

    public static EventFormat BuildReassignmentEventFormat(
        RuntimeCommands.SubmitterInfo submitter,
        ProtoIdentifier filterId,
        bool isInterface) =>
        BuildEventFormat(submitter, filterId, isInterface);

    private static EventFormat BuildEventFormat(
        RuntimeCommands.SubmitterInfo submitter,
        ProtoIdentifier filterId,
        bool isInterface)
    {
        var eventFormat = new EventFormat { Verbose = true };
        Func<Filters> createFilters = isInterface
            ? () => BuildInterfaceFilters(filterId)
            : () => BuildTemplateFilters(filterId);

        AddFilterForEachParty(eventFormat, submitter, createFilters);
        return eventFormat;
    }

    private static void AddFilterForEachParty(
        EventFormat eventFormat,
        RuntimeCommands.SubmitterInfo submitter,
        Func<Filters> createFilters)
    {
        foreach (var partyId in SubscribeFilterPolicy.FilteredPartyIds(submitter))
        {
            eventFormat.FiltersByParty.Add(partyId, createFilters());
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

    private static Filters BuildInterfaceFilters(ProtoIdentifier interfaceId)
    {
        var filters = new Filters();
        filters.Cumulative.Add(new CumulativeFilter
        {
            InterfaceFilter = new InterfaceFilter
            {
                InterfaceId = interfaceId,
                IncludeInterfaceView = true,
            },
        });
        return filters;
    }
}
