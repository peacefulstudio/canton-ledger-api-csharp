// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Canton.Ledger.Pqs.Client;

internal sealed partial class PqsHealthCheck(IOptions<PqsClientOptions> options, ILogger<PqsHealthCheck>? logger = null) : IHealthCheck
{
    private readonly PqsClientOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    private readonly ILogger<PqsHealthCheck> _logger = logger ?? NullLogger<PqsHealthCheck>.Instance;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = new NpgsqlConnection(_options.ConnectionString);
            await using (connection.ConfigureAwait(false))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                var command = new NpgsqlCommand("SELECT 1", connection);
                await using (command.ConfigureAwait(false))
                {
                    await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

                    LogHealthy(_logger, connection.Database ?? "unknown");

                    return HealthCheckResult.Healthy(
                        description: "PQS database is reachable.",
                        data: new Dictionary<string, object> { ["database"] = connection.Database ?? "unknown" });
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnhealthy(_logger, ex);

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
