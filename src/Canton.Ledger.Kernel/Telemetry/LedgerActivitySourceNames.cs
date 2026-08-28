// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Kernel.Telemetry;

/// <summary>
/// The well-known <see cref="System.Diagnostics.ActivitySource"/> names of every Canton ledger
/// client, so a host can register the whole set without referencing any concrete client assembly.
/// Each name is the client type's fully qualified name, per
/// <see cref="LedgerActivitySource.NameFor{T}"/>; the clients expose the same string as their own
/// <c>ActivitySourceName</c>, and each client's test suite pins the two together.
/// </summary>
/// <remarks>
/// The kernel is a peer of the transports and references none of them, so these are
/// string literals rather than <c>typeof(T).FullName</c>. Renaming or moving a client type is
/// therefore a two-file change — the type and the literal here — which the per-client guard tests
/// turn from a silent telemetry outage into a build-time failure.
/// </remarks>
public static class LedgerActivitySourceNames
{
    /// <summary>The gRPC <c>Canton.Ledger.Grpc.Client.LedgerClient</c> source.</summary>
    public const string GrpcLedgerClient = "Canton.Ledger.Grpc.Client.LedgerClient";

    /// <summary>The gRPC <c>Canton.Ledger.Grpc.Client.AdminClient</c> source.</summary>
    public const string GrpcAdminClient = "Canton.Ledger.Grpc.Client.AdminClient";

    /// <summary>The JSON Ledger API <c>Canton.Ledger.Rest.Client.RestLedgerClient</c> source.</summary>
    public const string RestLedgerClient = "Canton.Ledger.Rest.Client.RestLedgerClient";

    /// <summary>The PQS <c>Canton.Ledger.Pqs.Client.PqsClient</c> source.</summary>
    public const string PqsClient = "Canton.Ledger.Pqs.Client.PqsClient";

    /// <summary>
    /// Every well-known client source name. <c>Canton.Ledger.OpenTelemetry</c> registers this whole
    /// set, so a client added here is instrumented without touching the OpenTelemetry package.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        GrpcLedgerClient,
        GrpcAdminClient,
        RestLedgerClient,
        PqsClient
    ];
}
