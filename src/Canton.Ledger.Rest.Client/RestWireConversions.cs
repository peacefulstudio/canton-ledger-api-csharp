// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Daml.Runtime.Data;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using WireIdentifier = Canton.Ledger.Rest.Identifier;

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

    public static long ParseOffset(string? wireOffset) =>
        long.TryParse(wireOffset, NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
            ? offset
            : throw new FormatException($"Cannot parse wire offset '{wireOffset}' as a non-negative integer.");

    public static bool IsModuleEntityMatch(WireIdentifier? candidate, RuntimeIdentifier expected)
    {
        if (candidate is null) return false;
        return string.Equals(candidate.ModuleName, expected.ModuleName, StringComparison.Ordinal)
            && string.Equals(candidate.EntityName, expected.EntityName, StringComparison.Ordinal);
    }
}
