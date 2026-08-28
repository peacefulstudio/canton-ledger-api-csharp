// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Extension methods for registering JSON Ledger API health checks.
/// </summary>
public static class HealthCheckBuilderExtensions
{
    /// <summary>
    /// Adds a health check that verifies connectivity to the Canton participant node over HTTP by
    /// querying the ledger end via <see cref="ILedgerReader.GetLedgerEndAsync"/>. Requires
    /// <see cref="RestLedgerClient"/> to be registered in the service collection (e.g. via
    /// <see cref="ServiceCollectionExtensions.AddRestLedgerClient(IServiceCollection, IConfiguration)"/>).
    /// </summary>
    /// <remarks>
    /// The check resolves the concrete <see cref="RestLedgerClient"/> rather than
    /// <see cref="ILedgerClient"/>, so a host wiring both transports gets a check that probes the
    /// HTTP endpoint specifically instead of whichever transport won the interface registration.
    /// <see cref="ILedgerReader.GetLedgerEndAsync"/> is not gated behind <c>participant_admin</c>,
    /// so the check succeeds for a reachable participant regardless of whether the caller holds
    /// admin rights.
    /// </remarks>
    public static IHealthChecksBuilder AddRestLedgerClient(
        this IHealthChecksBuilder builder,
        string name = "canton-ledger-rest",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp => ActivatorUtilities.CreateInstance<RestLedgerHealthCheck>(sp),
            failureStatus,
            tags,
            timeout));
    }
}
