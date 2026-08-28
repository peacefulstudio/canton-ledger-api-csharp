// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Daml.Runtime.Data;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using WireIdentifier = Canton.Ledger.Rest.Client.Raw.Identifier;

namespace Canton.Ledger.Rest.Client;

internal static class RestWireConversions
{
    public static IReadOnlyList<Party> ToPartyList(IEnumerable<string>? wireParties)
    {
        var result = new List<Party>();
        if (wireParties is null) return result;
        foreach (var party in wireParties)
        {
            result.Add((Party)party);
        }
        return result;
    }

    public static RuntimeIdentifier ToRuntimeIdentifier(WireIdentifier identifier) =>
        new(identifier.PackageId, identifier.ModuleName, identifier.EntityName);

    public static long ParseOffset(string? wireOffset) => ParseNonNegativeInt64(wireOffset, "offset");

    public static long ParseReassignmentCounter(string? wireCounter) =>
        ParseNonNegativeInt64(wireCounter, "reassignment counter");

    public static bool TryParseOffset(string? wireOffset, out long offset) =>
        long.TryParse(wireOffset, NumberStyles.None, CultureInfo.InvariantCulture, out offset);

    public static bool IsModuleEntityMatch(WireIdentifier? candidate, RuntimeIdentifier expected)
    {
        if (candidate is null) return false;
        return string.Equals(candidate.ModuleName, expected.ModuleName, StringComparison.Ordinal)
            && string.Equals(candidate.EntityName, expected.EntityName, StringComparison.Ordinal);
    }

    public static bool IsModuleEntityMatch(RuntimeIdentifier? candidate, RuntimeIdentifier expected)
    {
        if (candidate is null) return false;
        return string.Equals(candidate.ModuleName, expected.ModuleName, StringComparison.Ordinal)
            && string.Equals(candidate.EntityName, expected.EntityName, StringComparison.Ordinal);
    }

    private static long ParseNonNegativeInt64(string? wireValue, string fieldName) =>
        long.TryParse(wireValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException($"Cannot parse wire {fieldName} '{wireValue}' as a non-negative integer.");
}
