// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Canton.Ledger.Auth;
using Canton.Ledger.Grpc.Client;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void add_ledger_client_registers_iledger_client_as_singleton()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GrpcAddress"] = "https://localhost:5001"
            })
            .Build();

        services.AddLedgerClient(config);

        var descriptor = services.Should().ContainSingle(d => d.ServiceType == typeof(ILedgerClient)).Subject;
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
        descriptor.ImplementationType.Should().Be<LedgerClient>();
    }

    [Fact]
    public void add_admin_client_registers_iadmin_client_as_singleton()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GrpcAddress"] = "https://localhost:5001"
            })
            .Build();

        services.AddAdminClient(config);

        var descriptor = services.Should().ContainSingle(d => d.ServiceType == typeof(IAdminClient)).Subject;
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
        descriptor.ImplementationType.Should().Be<AdminClient>();
    }

    [Fact]
    public void add_ledger_client_binds_options_from_configuration()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GrpcAddress"] = "https://localhost:5001",
                ["UserId"] = "test-user"
            })
            .Build();

        services.AddLedgerClient(config);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LedgerClientOptions>>();
        options.Value.GrpcAddress.Should().Be("https://localhost:5001");
        options.Value.UserId.Should().Be("test-user");
    }

    [Fact]
    public void add_ledger_client_throws_for_null_services()
    {
        IServiceCollection services = null!;
        var config = new ConfigurationBuilder().Build();

        var act = () => services.AddLedgerClient(config);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void add_ledger_client_throws_for_null_configuration()
    {
        var services = new ServiceCollection();

        var act = () => services.AddLedgerClient((IConfiguration)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void add_admin_client_throws_for_null_services()
    {
        IServiceCollection services = null!;
        var config = new ConfigurationBuilder().Build();

        var act = () => services.AddAdminClient(config);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void add_admin_client_throws_for_null_configuration()
    {
        var services = new ServiceCollection();

        var act = () => services.AddAdminClient((IConfiguration)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void add_ledger_client_returns_services_for_chaining()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GrpcAddress"] = "https://localhost:5001"
        }).Build();

        var result = services.AddLedgerClient(config);

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void add_admin_client_returns_services_for_chaining()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GrpcAddress"] = "https://localhost:5001"
        }).Build();

        var result = services.AddAdminClient(config);

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void ledger_client_resolves_when_token_provider_registered()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GrpcAddress"] = "https://localhost:5001"
        }).Build();

        services.AddCantonStaticAuth("test-token");
        services.AddLedgerClient(config);

        var provider = services.BuildServiceProvider();
        var client = provider.GetService<ILedgerClient>();

        client.Should().NotBeNull();
        client.Should().BeOfType<LedgerClient>();
    }

    [Fact]
    public void admin_client_resolves_when_token_provider_registered()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GrpcAddress"] = "https://localhost:5001"
        }).Build();

        services.AddCantonStaticAuth("test-token");
        services.AddAdminClient(config);

        var provider = services.BuildServiceProvider();
        var client = provider.GetService<IAdminClient>();

        client.Should().NotBeNull();
        client.Should().BeOfType<AdminClient>();
    }

    [Fact]
    public void ledger_client_fails_without_token_provider()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GrpcAddress"] = "https://localhost:5001"
        }).Build();

        services.AddLedgerClient(config);

        var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<ILedgerClient>();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void admin_client_fails_without_token_provider()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GrpcAddress"] = "https://localhost:5001"
        }).Build();

        services.AddAdminClient(config);

        var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IAdminClient>();

        act.Should().Throw<InvalidOperationException>();
    }
}
