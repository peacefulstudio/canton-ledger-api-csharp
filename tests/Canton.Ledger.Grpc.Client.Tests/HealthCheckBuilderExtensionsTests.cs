// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Ledger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
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
    public async Task AddLedgerClient_health_check_logs_the_failed_probe_to_the_registered_ILoggerFactory()
    {
        var loggerFactory = new CapturingLoggerFactory();
        var ledgerClient = Substitute.For<ILedgerClient>();
        ledgerClient.GetLedgerEndAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Connection refused"));

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton(ledgerClient);
        services.AddHealthChecks().AddLedgerClient();

        var provider = services.BuildServiceProvider();
        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Single();
        var healthCheck = registration.Factory(provider);

        await healthCheck.CheckHealthAsync(
            new HealthCheckContext { Registration = registration },
            TestContext.Current.CancellationToken);

        loggerFactory.Records.Should().Contain(r =>
            r.Category == typeof(LedgerHealthCheck).FullName
            && r.Level == LogLevel.Warning
            && r.Message.Contains("Ledger health check failed"));
    }

    [Fact]
    public void AddLedgerClient_throws_for_null_builder()
    {
        IHealthChecksBuilder builder = null!;

        var act = () => builder.AddLedgerClient();

        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }
}
