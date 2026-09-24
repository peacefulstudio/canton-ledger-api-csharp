// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Runtime.InteropServices;
using Canton.Ledger.Grpc.Client;

namespace Canton.Ledger.Benchmarks;

internal sealed record BenchmarkEnvironment(
    DateTimeOffset StartedAt,
    string Commit,
    string Runner,
    string OperatingSystem,
    string Architecture,
    int ProcessorCount,
    string DotnetRuntime,
    string ClientVersion,
    string CantonProtoVersion,
    string LedgerApiVersion,
    bool PqsMeasured)
{
    public static BenchmarkEnvironment Capture(BenchmarkOptions options, string ledgerApiVersion, bool pqsMeasured) => new(
        DateTimeOffset.UtcNow,
        options.Commit,
        options.Runner,
        RuntimeInformation.OSDescription,
        RuntimeInformation.OSArchitecture.ToString(),
        Environment.ProcessorCount,
        RuntimeInformation.FrameworkDescription,
        typeof(LedgerClientOptions).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
        typeof(BenchmarkEnvironment).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "CantonVersion")?.Value ?? "unknown",
        ledgerApiVersion,
        pqsMeasured);
}
