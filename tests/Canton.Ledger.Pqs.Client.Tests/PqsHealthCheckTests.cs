// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
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

        var result = await healthCheck.CheckHealthAsync(CreateContext(HealthStatus.Degraded), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckHealth_uses_registration_failure_status_on_error()
    {
        var options = Options.Create(new PqsClientOptions
        {
            ConnectionString = "Host=127.0.0.1;Port=1;Database=pqs;Timeout=1"
        });

        var healthCheck = new PqsHealthCheck(options);

        var result = await healthCheck.CheckHealthAsync(CreateContext(HealthStatus.Unhealthy), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("PQS database is unreachable.");
    }

    [Fact]
    public async Task CheckHealth_propagates_OperationCanceledException_on_cancellation()
    {
        var options = Options.Create(new PqsClientOptions
        {
            ConnectionString = "Host=127.0.0.1;Port=1;Database=pqs;Timeout=30"
        });
        var healthCheck = new PqsHealthCheck(options);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await healthCheck.CheckHealthAsync(CreateContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CheckHealth_dials_the_injected_data_source_instead_of_the_connection_string()
    {
        var options = Options.Create(new PqsClientOptions
        {
            ConnectionString = "Host=127.0.0.1;Port=1;Database=pqs;Timeout=1"
        });
        await using var dataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Port=2;Database=pqs;Timeout=1");
        var healthCheck = new PqsHealthCheck(options, dataSource);

        var result = await healthCheck.CheckHealthAsync(CreateContext(), TestContext.Current.CancellationToken);

        result.Exception.Should().NotBeNull();
        result.Exception!.ToString().Should().Contain("127.0.0.1:2").And.NotContain("127.0.0.1:1");
    }

    [Fact]
    public async Task CheckHealth_resolved_from_DI_dials_the_registered_data_source()
    {
        var services = new ServiceCollection();
        services.AddPqsClient(o => o.ConnectionString = "Host=127.0.0.1;Port=1;Database=pqs;Timeout=1");
        services.AddSingleton(NpgsqlDataSource.Create("Host=127.0.0.1;Port=2;Database=pqs;Timeout=1"));
        services.AddHealthChecks().AddPqsClient();
        await using var provider = services.BuildServiceProvider();
        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Single(r => r.Name == "pqs");
        var healthCheck = registration.Factory(provider);

        var result = await healthCheck.CheckHealthAsync(CreateContext(), TestContext.Current.CancellationToken);

        result.Exception!.ToString().Should().Contain("127.0.0.1:2");
    }

    [Fact]
    public void Constructor_throws_for_null_options()
    {
        var act = () => new PqsHealthCheck(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }
}
