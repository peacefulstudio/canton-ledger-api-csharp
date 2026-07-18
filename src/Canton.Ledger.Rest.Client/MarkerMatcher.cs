// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using WireCreatedEvent = Canton.Ledger.Rest.CreatedEvent;

namespace Canton.Ledger.Rest.Client;

internal static class MarkerMatcher<TMarker>
    where TMarker : IDamlType
{
    public static bool IsInterface { get; } = TMarker.DamlTypeId.Kind == DamlTypeKind.Interface;

    private static readonly RuntimeIdentifier MarkerIdentity = TMarker.DamlTypeId.Identifier;

    public static bool MatchesCreated(WireCreatedEvent created) =>
        IsInterface
            ? MatchesAnyIdentifier(created.InterfaceViews?.Select(view => view?.InterfaceId))
            : RestWireConversions.IsModuleEntityMatch(created.TemplateId, MarkerIdentity);

    private static bool MatchesAnyIdentifier(IEnumerable<Rest.Identifier?>? identifiers)
    {
        if (identifiers is null) return false;
        foreach (var identifier in identifiers)
        {
            if (RestWireConversions.IsModuleEntityMatch(identifier, MarkerIdentity)) return true;
        }
        return false;
    }
}
