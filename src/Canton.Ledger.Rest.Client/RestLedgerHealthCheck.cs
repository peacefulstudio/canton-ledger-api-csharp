// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Canton.Ledger.Rest.Client;

internal sealed partial class RestLedgerHealthCheck(
    RestLedgerClient restLedgerClient,
    ILogger<RestLedgerHealthCheck>? logger = null) : IHealthCheck
{
    private readonly RestLedgerClient _restLedgerClient = restLedgerClient ?? throw new ArgumentNullException(nameof(restLedgerClient));
    private readonly ILogger<RestLedgerHealthCheck> _logger = logger ?? NullLogger<RestLedgerHealthCheck>.Instance;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var ledgerEnd = (await _restLedgerClient.GetLedgerEndAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).Value;

            LogHealthy(_logger, ledgerEnd);

            return HealthCheckResult.Healthy(
                description: $"Canton participant is reachable over HTTP (ledger end offset {ledgerEnd}).",
                data: new Dictionary<string, object> { ["ledgerEnd"] = ledgerEnd });
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Unhealthy(context, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Unhealthy(context, ex);
        }
    }

    private HealthCheckResult Unhealthy(HealthCheckContext context, Exception ex)
    {
        LogUnhealthy(_logger, ex);

        return new HealthCheckResult(
            context.Registration.FailureStatus,
            description: "Canton participant node is unreachable over HTTP.",
            exception: ex);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "REST ledger health check passed at ledger end offset {LedgerEnd}")]
    private static partial void LogHealthy(ILogger logger, long ledgerEnd);

    [LoggerMessage(Level = LogLevel.Warning, Message = "REST ledger health check failed")]
    private static partial void LogUnhealthy(ILogger logger, Exception ex);
}
