// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

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
    /// by retrieving the participant ID via the admin API.
    /// Requires <see cref="IAdminClient"/> to be registered in the service collection
    /// (e.g., via <see cref="ServiceCollectionExtensions.AddAdminClient(IServiceCollection, IConfiguration)"/>).
    /// </summary>
    /// <remarks>
    /// Despite the method name, this health check depends on <see cref="IAdminClient"/>
    /// (not <see cref="ILedgerClient"/>) because it calls
    /// <see cref="IAdminClient.GetParticipantIdAsync"/> to verify connectivity.
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
