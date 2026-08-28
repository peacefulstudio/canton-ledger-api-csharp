// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Telemetry;
using Npgsql;

namespace OpenTelemetry.Trace;

/// <summary>
/// Registers the Canton Ledger API clients' <see cref="System.Diagnostics.ActivitySource"/>s
/// with an OpenTelemetry <see cref="TracerProviderBuilder"/>.
/// </summary>
public static class CantonLedgerTracerProviderBuilderExtensions
{
    /// <summary>
    /// Registers OpenTelemetry tracing for every Canton Ledger API client — the gRPC
    /// <c>LedgerClient</c>/<c>AdminClient</c>, the JSON <c>RestLedgerClient</c>, and the PQS
    /// <c>PqsClient</c> <see cref="System.Diagnostics.ActivitySource"/>s named by
    /// <see cref="LedgerActivitySourceNames.All"/> — plus Npgsql's own PostgreSQL instrumentation
    /// for the PQS client's underlying queries. Equivalent to calling
    /// <c>.AddSource([.. LedgerActivitySourceNames.All]).AddNpgsql()</c> by hand.
    /// </summary>
    /// <remarks>
    /// The source names come from <c>Canton.Ledger.Kernel</c>, so this package references no
    /// concrete client assembly: a host tracing only the REST client does not drag in the gRPC
    /// stack, and a client added to the kernel's list is instrumented without a change here.
    /// This package is also the only place in the Canton Ledger API client libraries that takes an
    /// OpenTelemetry SDK dependency — the clients themselves emit only BCL
    /// <see cref="System.Diagnostics.Activity"/> spans and take no OpenTelemetry package reference,
    /// so a consumer who does not call this method pays no OpenTelemetry cost at all.
    /// </remarks>
    /// <param name="builder">The tracer provider builder to register the Canton sources on.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    public static TracerProviderBuilder AddCantonLedgerInstrumentation(this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .AddSource([.. LedgerActivitySourceNames.All])
            .AddNpgsql();
    }
}
