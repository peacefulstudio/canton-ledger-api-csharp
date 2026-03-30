// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Canton.Ledger.Pqs.Client;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class HealthCheckBuilderExtensionsTests
{
    [Fact]
    public void add_pqs_client_registers_health_check_with_default_name()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddPqsClient();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();

        options.Value.Registrations.Should().ContainSingle(r => r.Name == "pqs");
    }

    [Fact]
    public void add_pqs_client_registers_health_check_with_custom_name_and_tags()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddPqsClient(
            name: "pqs-custom",
            tags: ["database", "ready"]);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();

        var registration = options.Value.Registrations.Should().ContainSingle(r => r.Name == "pqs-custom").Subject;
        registration.Tags.Should().Contain(["database", "ready"]);
    }

    [Fact]
    public void add_pqs_client_throws_for_null_builder()
    {
        IHealthChecksBuilder builder = null!;

        var act = () => builder.AddPqsClient();

        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }
}
