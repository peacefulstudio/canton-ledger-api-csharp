// Copyright © 2026 Peaceful Studio OÜ. All rights reserved.

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class HealthCheckBuilderExtensionsTests
{
    [Fact]
    public void AddLedgerClient_registers_health_check_with_default_name()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddLedgerClient();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();

        options.Value.Registrations.Should().ContainSingle(r => r.Name == "canton-ledger");
    }

    [Fact]
    public void AddLedgerClient_registers_health_check_with_custom_name_and_tags()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddLedgerClient(
            name: "ledger-custom",
            tags: ["grpc", "ready"]);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();

        var registration = options.Value.Registrations.Should().ContainSingle(r => r.Name == "ledger-custom").Subject;
        registration.Tags.Should().Contain(["grpc", "ready"]);
    }

    [Fact]
    public void AddLedgerClient_throws_for_null_builder()
    {
        IHealthChecksBuilder builder = null!;

        var act = () => builder.AddLedgerClient();

        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }
}
