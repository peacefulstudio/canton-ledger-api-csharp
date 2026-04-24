// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class PqsHealthCheckTests
{
    private static HealthCheckContext CreateContext(HealthStatus failureStatus = HealthStatus.Unhealthy) =>
        new()
        {
            Registration = new HealthCheckRegistration("pqs", Substitute.For<IHealthCheck>(), failureStatus, null)
        };

    [Fact]
    public async Task CheckHealth_returns_failure_when_connection_fails()
    {
        var options = Options.Create(new PqsClientOptions
        {
            ConnectionString = "Host=127.0.0.1;Port=1;Database=pqs;Timeout=1"
        });

        var healthCheck = new PqsHealthCheck(options);

        var result = await healthCheck.CheckHealthAsync(CreateContext(HealthStatus.Degraded));

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Exception.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_throws_for_null_options()
    {
        var act = () => new PqsHealthCheck(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }
}
