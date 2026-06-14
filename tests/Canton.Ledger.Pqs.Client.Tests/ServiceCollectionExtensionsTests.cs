// Copyright 2026 Peaceful Studio OÜ

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class ServiceCollectionExtensionsTests
{
    private static IConfiguration ConfigWith(string? connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionString"] = connectionString
            })
            .Build();

    [Fact]
    public void AddPqsClient_configuration_registers_IPqsClient_as_singleton()
    {
        var services = new ServiceCollection();

        services.AddPqsClient(ConfigWith("Host=localhost;Database=pqs"));

        var descriptor = services.Should().ContainSingle(d => d.ServiceType == typeof(IPqsClient)).Subject;
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
        descriptor.ImplementationType.Should().Be<PqsClient>();
    }

    [Fact]
    public void AddPqsClient_configuration_binds_options_from_configuration()
    {
        var services = new ServiceCollection();
        services.AddPqsClient(ConfigWith("Host=localhost;Database=pqs"));

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PqsClientOptions>>();

        options.Value.ConnectionString.Should().Be("Host=localhost;Database=pqs");
    }

    [Fact]
    public void AddPqsClient_configuration_returns_services_for_chaining()
    {
        var services = new ServiceCollection();
        var result = services.AddPqsClient(new ConfigurationBuilder().Build());

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddPqsClient_configuration_throws_for_null_services()
    {
        IServiceCollection services = null!;

        var act = () => services.AddPqsClient(new ConfigurationBuilder().Build());

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddPqsClient_configuration_throws_for_null_configuration()
    {
        var services = new ServiceCollection();

        var act = () => services.AddPqsClient((IConfiguration)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddPqsClient_configuration_validates_missing_ConnectionString_at_startup()
    {
        var services = new ServiceCollection();
        services.AddPqsClient(ConfigWith(null));

        var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<PqsClientOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().Contain(f => f.Contains(nameof(PqsClientOptions.ConnectionString), StringComparison.Ordinal));
    }

    [Fact]
    public void AddPqsClient_action_registers_IPqsClient_as_singleton()
    {
        var services = new ServiceCollection();

        services.AddPqsClient(o => o.ConnectionString = "Host=localhost;Database=pqs");

        var descriptor = services.Should().ContainSingle(d => d.ServiceType == typeof(IPqsClient)).Subject;
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
        descriptor.ImplementationType.Should().Be<PqsClient>();
    }

    [Fact]
    public void AddPqsClient_action_configures_options_from_delegate()
    {
        var services = new ServiceCollection();
        services.AddPqsClient(o => o.ConnectionString = "Host=example;Database=pqs");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PqsClientOptions>>();

        options.Value.ConnectionString.Should().Be("Host=example;Database=pqs");
    }

    [Fact]
    public void AddPqsClient_action_returns_services_for_chaining()
    {
        var services = new ServiceCollection();
        var result = services.AddPqsClient(_ => { });

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddPqsClient_action_throws_for_null_services()
    {
        IServiceCollection services = null!;

        var act = () => services.AddPqsClient(_ => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddPqsClient_action_throws_for_null_configure()
    {
        var services = new ServiceCollection();

        var act = () => services.AddPqsClient((Action<PqsClientOptions>)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configure");
    }

    [Fact]
    public void AddPqsClient_action_validates_missing_ConnectionString_at_startup()
    {
        var services = new ServiceCollection();
        services.AddPqsClient(_ => { });

        var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<PqsClientOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void AddPqsClient_called_twice_keeps_single_IPqsClient_registration()
    {
        var services = new ServiceCollection();

        services.AddPqsClient(o => o.ConnectionString = "Host=first;Database=pqs");
        services.AddPqsClient(o => o.ConnectionString = "Host=second;Database=pqs");

        services.Where(d => d.ServiceType == typeof(IPqsClient)).Should().HaveCount(1);
    }

    [Fact]
    public void AddPqsClient_called_twice_applies_second_Configure_delegate()
    {
        var services = new ServiceCollection();

        services.AddPqsClient(o => o.ConnectionString = "Host=first;Database=pqs");
        services.AddPqsClient(o => o.ConnectionString = "Host=second;Database=pqs");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PqsClientOptions>>();

        options.Value.ConnectionString.Should().Be("Host=second;Database=pqs");
    }

    [Fact]
    public void AddPqsClient_resolves_PqsClient_as_IPqsClient()
    {
        var services = new ServiceCollection();
        services.AddPqsClient(o => o.ConnectionString = "Host=localhost;Database=pqs");

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IPqsClient>();

        client.Should().BeOfType<PqsClient>();
    }
}
