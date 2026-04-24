// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPqsClient_registers_IPqsClient_as_singleton()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionString"] = "Host=localhost;Database=pqs"
            })
            .Build();

        services.AddPqsClient(config);

        var descriptor = services.Should().ContainSingle(d => d.ServiceType == typeof(IPqsClient)).Subject;
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
        descriptor.ImplementationType.Should().Be<PqsClient>();
    }

    [Fact]
    public void AddPqsClient_binds_options_from_configuration()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionString"] = "Host=localhost;Database=pqs"
            })
            .Build();

        services.AddPqsClient(config);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PqsClientOptions>>();
        options.Value.ConnectionString.Should().Be("Host=localhost;Database=pqs");
    }

    [Fact]
    public void AddPqsClient_returns_services_for_chaining()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        var result = services.AddPqsClient(config);

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddPqsClient_throws_for_null_services()
    {
        IServiceCollection services = null!;
        var config = new ConfigurationBuilder().Build();

        var act = () => services.AddPqsClient(config);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddPqsClient_throws_for_null_configuration()
    {
        var services = new ServiceCollection();

        var act = () => services.AddPqsClient((IConfiguration)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }
}
