// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Pqs.Client;
using Npgsql;

namespace OpenTelemetry.Trace;

/// <summary>
/// Registers the Canton Ledger API clients' <see cref="System.Diagnostics.ActivitySource"/>s
/// with an OpenTelemetry <see cref="TracerProviderBuilder"/>.
/// </summary>
public static class CantonLedgerTracerProviderBuilderExtensions
{
    /// <summary>
    /// Registers OpenTelemetry tracing for the Canton Ledger API clients: the gRPC
    /// <see cref="LedgerClient"/>/<see cref="AdminClient"/> and PQS <see cref="PqsClient"/>
    /// <see cref="System.Diagnostics.ActivitySource"/>s, plus Npgsql's own PostgreSQL
    /// instrumentation for the PQS client's underlying queries. Equivalent to calling
    /// <c>.AddSource(LedgerClient.ActivitySourceName, AdminClient.ActivitySourceName, PqsClient.ActivitySourceName).AddNpgsql()</c>
    /// by hand. This package is the only place in the Canton Ledger API client libraries that
    /// takes an OpenTelemetry SDK dependency (ADR 0010) — the clients themselves emit only BCL
    /// <see cref="System.Diagnostics.Activity"/> spans and take no OpenTelemetry package reference,
    /// so a consumer who does not call this method pays no OpenTelemetry cost at all.
    /// </summary>
    /// <param name="builder">The tracer provider builder to register the Canton sources on.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    public static TracerProviderBuilder AddCantonLedgerInstrumentation(this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .AddSource(LedgerClient.ActivitySourceName, AdminClient.ActivitySourceName, PqsClient.ActivitySourceName)
            .AddNpgsql();
    }
}
