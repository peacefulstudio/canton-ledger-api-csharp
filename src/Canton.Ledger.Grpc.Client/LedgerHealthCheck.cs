// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Peaceful.Extensions.Logging;

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// Health check that verifies connectivity to the Canton participant node
/// by calling <see cref="IAdminClient.GetParticipantIdAsync"/>.
/// </summary>
internal sealed partial class LedgerHealthCheck : IHealthCheck
{
    private static readonly ILogger<LedgerHealthCheck> Logger = StaticLoggerFactory.Create<LedgerHealthCheck>();

    private readonly IAdminClient _adminClient;

    public LedgerHealthCheck(IAdminClient adminClient)
    {
        ArgumentNullException.ThrowIfNull(adminClient);
        _adminClient = adminClient;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var participantId = await _adminClient.GetParticipantIdAsync(cancellationToken);

            LogHealthy(Logger, participantId);

            return HealthCheckResult.Healthy(
                description: $"Canton participant {participantId} is reachable.",
                data: new Dictionary<string, object> { ["participantId"] = participantId });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnhealthy(Logger, ex);

            return new HealthCheckResult(
                context.Registration.FailureStatus,
                description: "Canton participant node is unreachable.",
                exception: ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Ledger health check passed for participant {ParticipantId}")]
    private static partial void LogHealthy(ILogger logger, string participantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ledger health check failed")]
    private static partial void LogUnhealthy(ILogger logger, Exception ex);
}
