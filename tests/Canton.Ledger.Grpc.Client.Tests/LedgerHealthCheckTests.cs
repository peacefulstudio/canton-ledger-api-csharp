// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerHealthCheckTests
{
    private static HealthCheckContext CreateContext(HealthStatus failureStatus = HealthStatus.Unhealthy) =>
        new()
        {
            Registration = new HealthCheckRegistration("canton-ledger", Substitute.For<IHealthCheck>(), failureStatus, null)
        };

    [Fact]
    public async Task CheckHealth_returns_healthy_with_participant_id()
    {
        var adminClient = Substitute.For<IAdminClient>();
        adminClient.GetParticipantIdAsync(Arg.Any<CancellationToken>())
            .Returns("participant-123");

        var healthCheck = new LedgerHealthCheck(adminClient);

        var result = await healthCheck.CheckHealthAsync(CreateContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("participantId")
            .WhoseValue.Should().Be("participant-123");
    }

    [Fact]
    public async Task CheckHealth_returns_failure_status_when_admin_client_throws()
    {
        var adminClient = Substitute.For<IAdminClient>();
        adminClient.GetParticipantIdAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Connection refused"));

        var healthCheck = new LedgerHealthCheck(adminClient);

        var result = await healthCheck.CheckHealthAsync(CreateContext(HealthStatus.Degraded));

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Exception.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task CheckHealth_propagates_operation_canceled_exception()
    {
        var adminClient = Substitute.For<IAdminClient>();
        adminClient.GetParticipantIdAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var healthCheck = new LedgerHealthCheck(adminClient);

        var act = () => healthCheck.CheckHealthAsync(CreateContext());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Constructor_throws_for_null_admin_client()
    {
        var act = () => new LedgerHealthCheck(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("adminClient");
    }
}
