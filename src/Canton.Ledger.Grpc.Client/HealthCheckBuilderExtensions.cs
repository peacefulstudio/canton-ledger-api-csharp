// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// Extension methods for registering Canton Ledger API health checks.
/// </summary>
public static class HealthCheckBuilderExtensions
{
    /// <summary>
    /// Adds a health check that verifies connectivity to the Canton participant node
    /// by querying the ledger end via <see cref="ILedgerReader.GetLedgerEndAsync"/>.
    /// Requires <see cref="ILedgerClient"/> to be registered in the service collection
    /// (e.g., via <see cref="ServiceCollectionExtensions.AddLedgerClient(IServiceCollection, IConfiguration)"/>).
    /// </summary>
    /// <remarks>
    /// <see cref="ILedgerReader.GetLedgerEndAsync"/> is not gated behind <c>participant_admin</c>,
    /// so the check succeeds for a reachable participant regardless of whether the caller holds
    /// admin rights — unlike a probe against an admin-only endpoint, which would report a
    /// healthy, least-privilege deployment as unreachable.
    /// </remarks>
    public static IHealthChecksBuilder AddLedgerClient(
        this IHealthChecksBuilder builder,
        string name = "canton-ledger",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp => ActivatorUtilities.CreateInstance<LedgerHealthCheck>(sp),
            failureStatus,
            tags,
            timeout));
    }
}
