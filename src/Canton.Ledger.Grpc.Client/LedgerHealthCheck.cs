// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Grpc.Core;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Canton.Ledger.Grpc.Client;

internal sealed partial class LedgerHealthCheck(ILedgerClient ledgerClient, ILogger<LedgerHealthCheck>? logger = null) : IHealthCheck
{
    private readonly ILedgerClient _ledgerClient = ledgerClient ?? throw new ArgumentNullException(nameof(ledgerClient));
    private readonly ILogger<LedgerHealthCheck> _logger = logger ?? NullLogger<LedgerHealthCheck>.Instance;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var ledgerEnd = (await _ledgerClient.GetLedgerEndAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).Value;

            LogHealthy(_logger, ledgerEnd);

            return HealthCheckResult.Healthy(
                description: $"Canton participant is reachable (ledger end offset {ledgerEnd}).",
                data: new Dictionary<string, object> { ["ledgerEnd"] = ledgerEnd });
        }
        catch (RpcException ex) when (CallerCancellation.Signals(ex, cancellationToken))
        {
            throw CallerCancellation.AsOperationCanceled(ex, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnhealthy(_logger, ex);

            return new HealthCheckResult(
                context.Registration.FailureStatus,
                description: "Canton participant node is unreachable.",
                exception: ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Ledger health check passed at ledger end offset {LedgerEnd}")]
    private static partial void LogHealthy(ILogger logger, long ledgerEnd);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ledger health check failed")]
    private static partial void LogUnhealthy(ILogger logger, Exception ex);
}
