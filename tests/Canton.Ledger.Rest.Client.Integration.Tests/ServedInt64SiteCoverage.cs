// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using Canton.Ledger.Rest.Client.Raw;

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// Classifies the <c>format: int64</c> property sites a participant's served OpenAPI document
/// declares against the sites <see cref="WireInt64Sites"/> reshapes. Only the served document
/// carries that discriminator: the vendored <c>spec/openapi.yaml</c> is derived from the Ledger API
/// protobuf definitions, and proto3 canonical JSON encodes an int64 as a string, so the generator
/// has nothing to mark and a reflection test over the generated surface can only rediscover the
/// names the table already lists.
/// </summary>
internal static class ServedInt64SiteCoverage
{
    private const string ServedSchemaNamePrefix = "Js";

    private static readonly FrozenDictionary<string, string> ServedSchemasTheGeneratedSurfaceNamesDifferently =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["JsAssignmentEvent"] = nameof(AssignedEvent),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenSet<string> RequestSitesTheParticipantAcceptsInTheDeclaredStringForm =
        new[]
        {
            "CompletionStreamRequest.beginExclusive",
            "GetActiveContractsPageRequest.activeAtOffset",
            "GetActiveContractsRequest.activeAtOffset",
            "GetCompletionsRequest.beginExclusive",
            "GetTransactionByOffsetRequest.offset",
            "GetUpdateByOffsetRequest.offset",
            "GetUpdatesPageRequest.beginOffsetExclusive",
            "GetUpdatesPageRequest.endOffsetInclusive",
            "GetUpdatesRequest.beginExclusive",
            "GetUpdatesRequest.endInclusive",
        }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> SitesAWholeTypeConverterAlreadyReshapes =
        new[]
        {
            "DeduplicationOffset.value",
            "DeduplicationOffset1.value",
            "DeduplicationOffset2.value",
        }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> SitesReadAsOnePartOfAServedDurationObject =
        new[]
        {
            "Duration.seconds",
        }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> SitesOnSchemasTheGeneratedSurfaceDeclaresNoTypeFor =
        new[]
        {
            "JsTransactionTree.offset",
        }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> ReshapedSites =
        WireInt64Sites.ByOwner
            .SelectMany(owner => owner.Value.Select(jsonName => $"{owner.Key.Name}.{jsonName}"))
            .ToFrozenSet(StringComparer.Ordinal);

    internal static IReadOnlyList<string> ExemptedSites() =>
        RequestSitesTheParticipantAcceptsInTheDeclaredStringForm
            .Concat(SitesAWholeTypeConverterAlreadyReshapes)
            .Concat(SitesReadAsOnePartOfAServedDurationObject)
            .Concat(SitesOnSchemasTheGeneratedSurfaceDeclaresNoTypeFor)
            .ToList();

    internal static IReadOnlyList<string> SitesNeitherReshapedNorExempt(IEnumerable<string> servedInt64Sites)
    {
        var exempted = ExemptedSites().ToFrozenSet(StringComparer.Ordinal);

        return servedInt64Sites
            .Where(site => !ReshapedSites.Contains(GeneratedSiteOf(site)))
            .Where(site => !exempted.Contains(site))
            .ToList();
    }

    private static string GeneratedSiteOf(string servedSite)
    {
        var separator = servedSite.IndexOf('.', StringComparison.Ordinal);
        return GeneratedTypeNameOf(servedSite[..separator]) + servedSite[separator..];
    }

    private static string GeneratedTypeNameOf(string servedSchemaName)
    {
        if (ServedSchemasTheGeneratedSurfaceNamesDifferently.TryGetValue(servedSchemaName, out var named))
            return named;

        var repeated = WithoutTapirRepeatSuffix(servedSchemaName);
        return repeated.StartsWith(ServedSchemaNamePrefix, StringComparison.Ordinal)
            ? repeated[ServedSchemaNamePrefix.Length..]
            : repeated;
    }

    private static string WithoutTapirRepeatSuffix(string servedSchemaName)
    {
        var end = servedSchemaName.Length;
        while (end > 0 && char.IsAsciiDigit(servedSchemaName[end - 1])) end--;
        return servedSchemaName[..end];
    }
}
