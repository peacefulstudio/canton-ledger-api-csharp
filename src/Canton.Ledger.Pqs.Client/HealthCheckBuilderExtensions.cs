// Copyright 2026 Peaceful Studio OÜ

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Canton.Ledger.Pqs.Client;

/// <summary>
/// Extension methods for registering PQS health checks.
/// </summary>
public static class HealthCheckBuilderExtensions
{
    /// <summary>
    /// Adds a health check that verifies connectivity to the PQS PostgreSQL database
    /// using the configured <see cref="PqsClientOptions.ConnectionString"/>.
    /// </summary>
    public static IHealthChecksBuilder AddPqsClient(
        this IHealthChecksBuilder builder,
        string name = "pqs",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp => ActivatorUtilities.CreateInstance<PqsHealthCheck>(sp),
            failureStatus,
            tags,
            timeout));
    }
}
