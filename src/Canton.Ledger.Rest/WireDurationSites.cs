// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Text.Json.Serialization.Metadata;
using Canton.Ledger.Rest.Client.Raw;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// The transport properties the Canton JSON Ledger API encodes as a <c>{"seconds":…,"nanos":…}</c>
/// object where our specification declares the proto3-canonical duration string. Every entry is a
/// <see cref="string"/> property on a named wire type, which is what keeps
/// <see cref="WireDurationJsonConverter"/> away from the Daml <c>Text</c> values inside a contract
/// payload.
/// <para>
/// <c>Commands.minLedgerTimeRel</c> is the whole table. The interactive-submission path carries its
/// bound in a <c>MinLedgerTime</c> wrapper the served document nests one level deeper still, under a
/// <c>time</c> <c>oneOf</c> whose selected arm wraps the duration in a <c>value</c> key; reshaping the
/// duration alone would not make that envelope bind, so that type is deliberately absent here.
/// </para>
/// </summary>
/// <remarks>
/// Retired by digital-asset/canton#527. Every entry here is one row of the compat overlay.
/// </remarks>
internal static class WireDurationSites
{
    internal static readonly FrozenDictionary<Type, FrozenSet<string>> ByOwner =
        new Dictionary<Type, string[]>
        {
            [typeof(Commands)] = ["minLedgerTimeRel"],
        }.ToFrozenDictionary(entry => entry.Key, entry => entry.Value.ToFrozenSet());

    /// <summary>
    /// Attaches <see cref="WireDurationJsonConverter"/> to the properties named in
    /// <see cref="ByOwner"/>, leaving every other property of every type untouched.
    /// </summary>
    internal static void UseWireEncoding(JsonTypeInfo typeInfo)
    {
        if (!ByOwner.TryGetValue(typeInfo.Type, out var jsonNames)) return;

        foreach (var property in typeInfo.Properties)
        {
            if (jsonNames.Contains(property.Name))
                property.CustomConverter = WireDurationJsonConverter.Instance;
        }
    }
}
