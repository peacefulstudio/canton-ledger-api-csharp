// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Text.Json.Serialization.Metadata;
using Canton.Ledger.Rest.Client.Raw;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// The transport properties the Canton JSON Ledger API encodes as raw JSON numbers where our
/// specification declares the proto3-canonical int64 string. Every entry is a <see cref="string"/>
/// property on a named wire type; no payload type appears here, which is what keeps
/// <see cref="WireInt64JsonConverter"/> away from the Daml <c>Int64</c> values inside
/// <c>createArgument</c>, <c>contractKey</c>, <c>choiceArgument</c> and <c>exerciseResult</c>.
/// </summary>
/// <remarks>
/// Retired by digital-asset/canton#527. Every entry here is one row of the compat overlay.
/// </remarks>
internal static class WireInt64Sites
{
    internal static readonly FrozenDictionary<Type, FrozenSet<string>> ByOwner =
        new Dictionary<Type, string[]>
        {
            [typeof(ActiveContract)] = ["reassignmentCounter"],
            [typeof(ArchivedEvent)] = ["offset"],
            [typeof(AssignedEvent)] = ["reassignmentCounter"],
            [typeof(Completion)] = ["offset", "paidTrafficCost"],
            [typeof(CostEstimation)] =
            [
                "confirmationRequestTrafficCostEstimation",
                "confirmationResponseTrafficCostEstimation",
                "totalTrafficCostEstimation",
            ],
            [typeof(CreatedEvent)] = ["offset"],
            [typeof(ExercisedEvent)] = ["offset"],
            [typeof(GetActiveContractsPageResponse)] = ["activeAtOffset"],
            [typeof(GetLatestPrunedOffsetsResponse)] =
                ["participantPrunedUpToInclusive", "allDivulgedContractsPrunedUpToInclusive"],
            [typeof(GetLedgerEndResponse)] = ["offset"],
            [typeof(GetUpdatesPageResponse)] = ["lowestPageOffsetExclusive", "highestPageOffsetInclusive"],
            [typeof(OffsetCheckpoint)] = ["offset"],
            [typeof(Reassignment)] = ["offset", "paidTrafficCost"],
            [typeof(SubmitAndWaitResponse)] = ["completionOffset"],
            [typeof(TopologyTransaction)] = ["offset"],
            [typeof(Transaction)] = ["offset", "paidTrafficCost"],
            [typeof(UnassignedEvent)] = ["offset", "reassignmentCounter"],
        }.ToFrozenDictionary(entry => entry.Key, entry => entry.Value.ToFrozenSet());

    /// <summary>
    /// Attaches <see cref="WireInt64JsonConverter"/> to the properties named in
    /// <see cref="ByOwner"/>, leaving every other property of every type untouched.
    /// </summary>
    internal static void UseWireEncoding(JsonTypeInfo typeInfo)
    {
        if (!ByOwner.TryGetValue(typeInfo.Type, out var jsonNames)) return;

        foreach (var property in typeInfo.Properties)
        {
            if (jsonNames.Contains(property.Name))
                property.CustomConverter = WireInt64JsonConverter.Instance;
        }
    }
}
