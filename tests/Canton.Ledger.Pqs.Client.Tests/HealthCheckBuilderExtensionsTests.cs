// Copyright 2026 Peaceful Studio OÜ

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class HealthCheckBuilderExtensionsTests
{
    private static HealthCheckRegistration Registration(IServiceCollection services, string name)
    {
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Single(r => r.Name == name);
    }

    [Fact]
    public void AddPqsClient_registers_health_check_with_default_name()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddPqsClient();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>();

        options.Value.Registrations.Should().ContainSingle(r => r.Name == "pqs");
    }

    [Fact]
    public void AddPqsClient_registers_health_check_with_custom_name_and_tags()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddPqsClient(
            name: "pqs-custom",
            tags: ["database", "ready"]);

        var registration = Registration(services, "pqs-custom");
        registration.Tags.Should().Contain(["database", "ready"]);
    }

    [Fact]
    public void AddPqsClient_passes_failureStatus_to_registration()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddPqsClient(failureStatus: HealthStatus.Degraded);

        Registration(services, "pqs").FailureStatus.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public void AddPqsClient_passes_timeout_to_registration()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddPqsClient(timeout: TimeSpan.FromSeconds(7));

        Registration(services, "pqs").Timeout.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public void AddPqsClient_factory_resolves_PqsHealthCheck_from_DI_options()
    {
        var services = new ServiceCollection();
        services.AddPqsClient(o => o.ConnectionString = "Host=localhost;Database=pqs");
        services.AddHealthChecks().AddPqsClient();

        var provider = services.BuildServiceProvider();
        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Single(r => r.Name == "pqs");

        var instance = registration.Factory(provider);

        instance.Should().BeOfType<PqsHealthCheck>();
    }

    [Fact]
    public void AddPqsClient_throws_for_null_builder()
    {
        IHealthChecksBuilder builder = null!;

        var act = () => builder.AddPqsClient();

        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }
}
