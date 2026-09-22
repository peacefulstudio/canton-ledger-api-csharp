// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Canton.Ledger.Kernel.Wire;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using WireIdentifier = Canton.Ledger.Rest.Client.Raw.Identifier;

namespace Canton.Ledger.Rest.Client;

internal static class RestWireConversions
{
    private const long NanosecondsPerTick = 100L;

    public static EquatableArray<Party> ToPartyList(IEnumerable<string>? wireParties) =>
        MalformedResponse.Decoding(wireParties, ToParties);

    private static EquatableArray<Party> ToParties(IEnumerable<string>? wireParties)
    {
        if (wireParties is null) return [];
        var result = new List<Party>();
        foreach (var party in wireParties)
        {
            result.Add((Party)party);
        }
        return EquatableArray.Create(result);
    }

    public static RuntimeIdentifier ToRuntimeIdentifier(WireIdentifier identifier) =>
        new(identifier.PackageId, identifier.ModuleName, identifier.EntityName);

    internal static string? ToKeyHash(string? contractKeyHash) =>
        string.IsNullOrEmpty(contractKeyHash) ? null : contractKeyHash;

    /// <remarks>
    /// The served document constrains a duration to the protobuf JSON encoding
    /// <c>^-?(?:0|[1-9][0-9]{0,11})(?:\.[0-9]{1,9})?s$</c> — whole seconds with an optional
    /// fraction of up to nine digits and no trailing zeros — which no BCL formatter produces.
    /// </remarks>
    public static string ToWireDuration(TimeSpan value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);

        var seconds = value.Ticks / TimeSpan.TicksPerSecond;
        var nanoseconds = value.Ticks % TimeSpan.TicksPerSecond * NanosecondsPerTick;

        return nanoseconds == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{seconds}s")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{seconds}.{nanoseconds.ToString("D9", CultureInfo.InvariantCulture).TrimEnd('0')}s");
    }

    public static long ParseOffset(string? wireOffset) => ParseNonNegativeInt64(wireOffset, "offset");

    public static long ParseReassignmentCounter(string? wireCounter) =>
        ParseNonNegativeInt64(wireCounter, "reassignment counter");

    public static long ParsePaidTrafficCost(string? wireCost) =>
        ParseNonNegativeInt64(wireCost, "paid traffic cost");

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
