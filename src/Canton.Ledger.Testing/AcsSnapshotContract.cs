// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Testing;

internal static class AcsSnapshotContract
{
    internal static AcsSnapshotEntry<T>[] Terminated<T>(AcsSnapshotEntry<T>[] entries, string parameterName)
        where T : ITemplate, IDamlRecord<T> =>
        Terminated(
            entries,
            entry => entry is AcsSnapshotEntry<T>.Checkpoint or AcsSnapshotEntry<T>.StreamError,
            $"WithActiveContracts<{typeof(T).Name}>",
            $"WithMalformedActiveContracts<{typeof(T).Name}>",
            parameterName);

    internal static InterfaceAcsSnapshotEntry<TInterface, TView>[] Terminated<TInterface, TView>(
        InterfaceAcsSnapshotEntry<TInterface, TView>[] entries,
        string parameterName)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        Terminated(
            entries,
            entry => entry is InterfaceAcsSnapshotEntry<TInterface, TView>.Checkpoint
                or InterfaceAcsSnapshotEntry<TInterface, TView>.StreamError,
            $"WithActiveInterfaceContracts<{typeof(TInterface).Name}, {typeof(TView).Name}>",
            $"WithMalformedActiveInterfaceContracts<{typeof(TInterface).Name}, {typeof(TView).Name}>",
            parameterName);

    private static TEntry[] Terminated<TEntry>(
        TEntry[] entries,
        Func<TEntry, bool> isTerminal,
        string stagingCall,
        string optOutCall,
        string parameterName)
    {
        var staged = entries.ToArray();
        var terminal = Array.FindIndex(staged, entry => isTerminal(entry));

        if (terminal < 0)
        {
            throw new ArgumentException(
                $"{stagingCall} stages {staged.Length} entries and no terminal Checkpoint or StreamError. A " +
                "participant always ends an active-contract-set snapshot with exactly one of those — the " +
                "Checkpoint even when the snapshot is empty — so a consumer that reads until the terminal entry " +
                $"never finishes reading this one. Append the terminal entry, or stage the snapshot through " +
                $"{optOutCall} when a shape no ledger can produce is what the test is about.",
                parameterName);
        }

        if (terminal != staged.Length - 1)
        {
            throw new ArgumentException(
                $"{stagingCall} stages {staged.Length - terminal - 1} {(staged.Length - terminal - 1 == 1 ? "entry" : "entries")} after the terminal entry at " +
                $"position {terminal}. A participant yields nothing after the terminal Checkpoint or StreamError, " +
                "and the two are mutually exclusive, so no snapshot has a second terminal entry or a row beyond " +
                $"the first. Keep the terminal entry last, or stage the snapshot through {optOutCall} when a shape " +
                "no ledger can produce is what the test is about.",
                parameterName);
        }

        return staged;
    }
}
