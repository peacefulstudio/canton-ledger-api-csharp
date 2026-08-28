// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestLedgerHealthCheckTests
{
    private static HealthCheckContext CreateContext(IHealthCheck healthCheck, HealthStatus failureStatus = HealthStatus.Unhealthy) =>
        new()
        {
            Registration = new HealthCheckRegistration("canton-ledger-rest", healthCheck, failureStatus, null)
        };

    private static RestLedgerHealthCheck HealthCheckOver(RecordingHttpHandler transport) =>
        new(new RestLedgerClient(new StubHttpClientFactory(transport)));

    [Fact]
    public async Task CheckHealthAsync_returns_healthy_with_the_ledger_end_offset()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, """{"offset":42}""");
        var healthCheck = HealthCheckOver(transport);

        var result = await healthCheck.CheckHealthAsync(CreateContext(healthCheck), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("ledgerEnd").WhoseValue.Should().Be(42L);
        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/state/ledger-end");
    }

    [Fact]
    public async Task CheckHealthAsync_returns_the_registered_failure_status_when_the_participant_is_unreachable()
    {
        var transport = new RecordingHttpHandler()
            .WithTransportException(new HttpRequestException("Connection refused"));
        var healthCheck = HealthCheckOver(transport);

        var result = await healthCheck.CheckHealthAsync(CreateContext(healthCheck, HealthStatus.Degraded), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Exception.Should().BeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task CheckHealthAsync_returns_the_registered_failure_status_on_an_error_response()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.ServiceUnavailable, "{}");
        var healthCheck = HealthCheckOver(transport);

        var result = await healthCheck.CheckHealthAsync(CreateContext(healthCheck), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_reports_a_request_timeout_as_unhealthy_rather_than_cancellation()
    {
        var transport = new RecordingHttpHandler()
            .WithTransportException(new TaskCanceledException("timed out", new TimeoutException()));
        var healthCheck = HealthCheckOver(transport);

        var result = await healthCheck.CheckHealthAsync(CreateContext(healthCheck), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeOfType<TaskCanceledException>();
    }

    [Fact]
    public async Task CheckHealthAsync_propagates_a_caller_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var transport = new RecordingHttpHandler()
            .WithTransportException(new TaskCanceledException("cancelled"));
        var healthCheck = HealthCheckOver(transport);

        var act = async () => await healthCheck.CheckHealthAsync(CreateContext(healthCheck), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void RestLedgerHealthCheck_rejects_a_null_client()
    {
        var act = () => new RestLedgerHealthCheck(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("restLedgerClient");
    }
}
