// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Kernel.Streams;

internal static class SubscribeFilterPolicy
{
    public static IEnumerable<string> FilteredPartyIds(RuntimeCommands.SubmitterInfo submitter) =>
        submitter.ActAs.Concat(submitter.ReadAs).Select(party => party.Id).Distinct();
}
