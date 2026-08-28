// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Canton.Ledger.Kernel.Streams;
using Daml.Runtime;
using RuntimeCommands = Daml.Runtime.Commands;
using WireCompletionStreamRequest = Canton.Ledger.Rest.Client.Raw.CompletionStreamRequest;
using WireCumulativeFilter = Canton.Ledger.Rest.Client.Raw.CumulativeFilter;
using WireEventFormat = Canton.Ledger.Rest.Client.Raw.EventFormat;
using WireFilters = Canton.Ledger.Rest.Client.Raw.Filters;
using WireGetActiveContractsRequest = Canton.Ledger.Rest.Client.Raw.GetActiveContractsRequest;
using WireGetUpdatesRequest = Canton.Ledger.Rest.Client.Raw.GetUpdatesRequest;
using WireIdentifierFilter = Canton.Ledger.Rest.Client.Raw.IdentifierFilter;
using WireInterfaceFilter = Canton.Ledger.Rest.Client.Raw.InterfaceFilter;
using WireTemplateFilter = Canton.Ledger.Rest.Client.Raw.TemplateFilter;
using WireTransactionFormat = Canton.Ledger.Rest.Client.Raw.TransactionFormat;
using WireUpdateFormat = Canton.Ledger.Rest.Client.Raw.UpdateFormat;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// The transaction shape a <c>POST /v2/updates</c> request selects: <see cref="AcsDelta"/> backs
/// the ACS-delta subscribe shape, <see cref="LedgerEffects"/> backs the ledger-effects shape.
/// </summary>
internal enum RestTransactionShape
{
    AcsDelta,
    LedgerEffects,
}

/// <summary>
/// Builds the wire request bodies for the JSON Ledger API's bounded, blocking stream endpoints
/// (<c>POST /v2/state/active-contracts</c>, <c>POST /v2/updates</c>,
/// <c>POST /v2/commands/completions</c>), mirroring the gRPC transport's
/// <c>SubscribeRequestBuilder</c>.
/// </summary>
internal static class RestSubscribeRequestBuilder
{
    public static WireGetActiveContractsRequest BuildGetActiveContractsRequest<T>(
        RuntimeCommands.SubmitterInfo submitter, long activeAtOffset)
        where T : IDamlType =>
        new()
        {
            ActiveAtOffset = FormatOffset(activeAtOffset),
            EventFormat = BuildEventFormat<T>(submitter),
        };

    public static WireGetUpdatesRequest BuildGetUpdatesRequest<T>(
        RuntimeCommands.SubmitterInfo submitter,
        long beginExclusive,
        long endInclusive,
        RestTransactionShape shape)
        where T : IDamlType
    {
        return new WireGetUpdatesRequest
        {
            BeginExclusive = FormatOffset(beginExclusive),
            EndInclusive = FormatOffset(endInclusive),
            UpdateFormat = new WireUpdateFormat
            {
                IncludeTransactions = new WireTransactionFormat
                {
                    EventFormat = BuildEventFormat<T>(submitter),
                    TransactionShape = shape == RestTransactionShape.LedgerEffects
                        ? Raw.TransactionFormatTransactionShape.TRANSACTION_SHAPE_LEDGER_EFFECTS
                        : Raw.TransactionFormatTransactionShape.TRANSACTION_SHAPE_ACS_DELTA,
                },
                IncludeReassignments = BuildReassignmentEventFormat<T>(submitter),
            },
        };
    }

    public static WireCompletionStreamRequest BuildCompletionStreamRequest(
        RuntimeCommands.SubmitterInfo submitter, long beginExclusive, string? userId)
    {
        var request = new WireCompletionStreamRequest
        {
            BeginExclusive = FormatOffset(beginExclusive),
            Parties = SubscribeFilterPolicy.FilteredPartyIds(submitter).ToList(),
        };

        if (userId is not null)
        {
            request.UserId = userId;
        }

        return request;
    }

    public static WireEventFormat BuildReassignmentEventFormat<T>(RuntimeCommands.SubmitterInfo submitter)
        where T : IDamlType =>
        BuildEventFormat<T>(submitter);

    public static WireUpdateFormat BuildTransactionUpdateFormat(RuntimeCommands.SubmitterInfo submitter) =>
        new()
        {
            IncludeTransactions = BuildTransactionFormat(submitter),
        };

    public static WireTransactionFormat BuildTransactionFormat(RuntimeCommands.SubmitterInfo submitter) =>
        new()
        {
            EventFormat = new WireEventFormat
            {
                Verbose = true,
                FiltersByParty = BuildFiltersByParty(
                    submitter, static () => new WireFilters { Cumulative = [] }),
            },
            TransactionShape = Raw.TransactionFormatTransactionShape.TRANSACTION_SHAPE_LEDGER_EFFECTS,
        };

    private static WireEventFormat BuildEventFormat<T>(RuntimeCommands.SubmitterInfo submitter)
        where T : IDamlType =>
        new()
        {
            Verbose = true,
            FiltersByParty = BuildFiltersByParty(submitter, BuildFilters<T>),
        };

    private static Dictionary<string, WireFilters> BuildFiltersByParty(
        RuntimeCommands.SubmitterInfo submitter, Func<WireFilters> createFilters) =>
        SubscribeFilterPolicy.FilteredPartyIds(submitter)
            .ToDictionary(partyId => partyId, _ => createFilters());

    private static WireFilters BuildFilters<T>()
        where T : IDamlType =>
        MarkerMatcher<T>.IsInterface ? BuildInterfaceFilters<T>() : BuildTemplateFilters<T>();

    private static WireFilters BuildTemplateFilters<T>()
        where T : IDamlType =>
        new()
        {
            Cumulative =
            [
                new WireCumulativeFilter
                {
                    IdentifierFilter = new WireIdentifierFilter
                    {
                        TemplateFilter = new WireTemplateFilter { TemplateId = MarkerMatcher<T>.FilterIdentifier },
                    },
                },
            ],
        };

    private static WireFilters BuildInterfaceFilters<T>()
        where T : IDamlType =>
        new()
        {
            Cumulative =
            [
                new WireCumulativeFilter
                {
                    IdentifierFilter = new WireIdentifierFilter
                    {
                        InterfaceFilter = new WireInterfaceFilter
                        {
                            InterfaceId = MarkerMatcher<T>.FilterIdentifier,
                            IncludeInterfaceView = true,
                        },
                    },
                },
            ],
        };

    private static string FormatOffset(long offset) => offset.ToString(CultureInfo.InvariantCulture);
}
