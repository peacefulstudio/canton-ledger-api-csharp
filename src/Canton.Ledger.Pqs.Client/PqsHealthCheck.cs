// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Canton.Ledger.Pqs.Client;

/// <summary>
/// Health check that verifies connectivity to the PQS PostgreSQL database.
/// </summary>
internal sealed partial class PqsHealthCheck : IHealthCheck
{
    private static readonly ILogger<PqsHealthCheck> Logger = LoggerFactory.Create<PqsHealthCheck>();

    private readonly PqsClientOptions _options;

    public PqsHealthCheck(IOptions<PqsClientOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);

            LogHealthy(Logger, connection.Database ?? "unknown");

            return HealthCheckResult.Healthy(
                description: "PQS database is reachable.",
                data: new Dictionary<string, object> { ["database"] = connection.Database ?? "unknown" });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnhealthy(Logger, ex);

            return new HealthCheckResult(
                context.Registration.FailureStatus,
                description: "PQS database is unreachable.",
                exception: ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "PQS health check passed for database {Database}")]
    private static partial void LogHealthy(ILogger logger, string database);

    [LoggerMessage(Level = LogLevel.Warning, Message = "PQS health check failed")]
    private static partial void LogUnhealthy(ILogger logger, Exception ex);
}
