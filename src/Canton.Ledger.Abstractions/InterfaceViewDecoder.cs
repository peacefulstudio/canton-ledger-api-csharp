// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Daml.Runtime.Data;

namespace Canton.Ledger.Abstractions;

internal static class InterfaceViewDecoder<TView>
    where TView : IDamlRecord
{
    private const string FactoryName = "FromRecord";

    private static readonly Func<DamlRecord, TView>? Factory = ResolveFactory();

    public static TView FromRecord(DamlRecord record) =>
        Factory is { } factory
            ? factory(record)
            : throw new InvalidOperationException(
                $"Interface view type {typeof(TView).FullName} exposes no "
                + $"'public static {typeof(TView).Name} {FactoryName}(DamlRecord)' factory, so a participant-computed "
                + "view cannot be decoded into it. Regenerate the view record with daml-codegen-csharp.");

    private static Func<DamlRecord, TView>? ResolveFactory()
    {
        var factory = typeof(TView).GetMethod(
            FactoryName,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            [typeof(DamlRecord)],
            modifiers: null);

        return factory?.ReturnType == typeof(TView)
            ? factory.CreateDelegate<Func<DamlRecord, TView>>()
            : null;
    }
}
