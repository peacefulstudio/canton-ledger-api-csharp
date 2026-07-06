// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Grpc;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

internal static class ReassignmentEventFormats
{
    public static EventFormat Wildcard(Party party)
    {
        var eventFormat = new EventFormat();
        eventFormat.FiltersByParty.Add(party.Id, new Filters
        {
            Cumulative = { new CumulativeFilter { WildcardFilter = new WildcardFilter() } },
        });
        return eventFormat;
    }

    public static EventFormat InterfaceFilterOn<TInterface>(Party party)
        where TInterface : IDamlInterface
    {
        var interfaceId = DamlValueConverter.ToProtoTemplateNameIdentifier(
            TInterface.PackageName, TInterface.InterfaceId);

        var eventFormat = new EventFormat();
        eventFormat.FiltersByParty.Add(party.Id, new Filters
        {
            Cumulative =
            {
                new CumulativeFilter
                {
                    InterfaceFilter = new InterfaceFilter
                    {
                        InterfaceId = interfaceId,
                        IncludeInterfaceView = false,
                    },
                },
            },
        });
        return eventFormat;
    }
}
