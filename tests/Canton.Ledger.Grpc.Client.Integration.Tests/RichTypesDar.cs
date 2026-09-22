// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Testing.Conformance;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

/// <summary>
/// The <c>richtypes</c> DAR from the <c>Daml.Codegen.Testing.Conformance</c> corpus, materialized once
/// per test process beside the test assembly so path-based uploads can reach it.
/// </summary>
internal static class RichTypesDar
{
    private static readonly Lazy<string> MaterializedPath = new(Materialize, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Gets the file-system path of the materialized corpus DAR.</summary>
    public static string Path => MaterializedPath.Value;

    private static string Materialize()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "conformance-richtypes.dar");
        using var dar = ConformanceCorpus.OpenDar(ConformancePackage.RichTypes);
        using var file = File.Create(path);
        dar.CopyTo(file);
        return path;
    }
}
