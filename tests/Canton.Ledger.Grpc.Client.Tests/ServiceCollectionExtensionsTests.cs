// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Canton.Ledger.Auth;
using Canton.Ledger.Auth.TokenGeneration;
using Daml.Ledger.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLedgerClient_registers_ILedgerClient_as_singleton()
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
    public void AddAdminClient_registers_IAdminClient_as_singleton()
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
    public void AddLedgerClient_binds_options_from_configuration()
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
    public void AddLedgerClient_throws_for_null_services()
    {
        IServiceCollection services = null!;
        var config = new ConfigurationBuilder().Build();

        var act = () => services.AddLedgerClient(config);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddLedgerClient_throws_for_null_configuration()
    {
        var services = new ServiceCollection();

        var act = () => services.AddLedgerClient((IConfiguration)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddAdminClient_throws_for_null_services()
    {
        IServiceCollection services = null!;
        var config = new ConfigurationBuilder().Build();

        var act = () => services.AddAdminClient(config);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddAdminClient_throws_for_null_configuration()
    {
        var services = new ServiceCollection();

        var act = () => services.AddAdminClient((IConfiguration)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddLedgerClient_returns_services_for_chaining()
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
    public void AddAdminClient_returns_services_for_chaining()
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
    public void AddLedgerClient_resolves_when_token_provider_registered()
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
    public void AddAdminClient_resolves_when_token_provider_registered()
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
    public void AddLedgerClient_auto_registers_client_credentials_from_auth_section()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GrpcAddress"] = "https://localhost:5001"
        }).Build();
        var authConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Domain"] = "https://auth.example.com",
            ["ClientId"] = "my-client",
            ["ClientSecret"] = "my-secret"
        }).Build();

        services.AddLedgerClient(config, authConfig);

        var provider = services.BuildServiceProvider();
        var tokenProvider = provider.GetRequiredService<ITokenProvider>();

        tokenProvider.Should().NotBeSameAs(ITokenProvider.None);
    }

    [Fact]
    public void AddAdminClient_auto_registers_client_credentials_from_auth_section()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GrpcAddress"] = "https://localhost:5001"
        }).Build();
        var authConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Domain"] = "https://auth.example.com",
            ["ClientId"] = "my-client",
            ["ClientSecret"] = "my-secret"
        }).Build();

        services.AddAdminClient(config, authConfig);

        var provider = services.BuildServiceProvider();
        var tokenProvider = provider.GetRequiredService<ITokenProvider>();

        tokenProvider.Should().NotBeSameAs(ITokenProvider.None);
    }

    [Fact]
    public void AddLedgerClient_explicit_token_provider_takes_precedence_over_auth_section()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GrpcAddress"] = "https://localhost:5001"
        }).Build();
        var authConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Domain"] = "https://auth.example.com",
            ["ClientId"] = "my-client",
            ["ClientSecret"] = "my-secret"
        }).Build();

        services.AddCantonStaticAuth("explicit-token");
        services.AddLedgerClient(config, authConfig);

        var provider = services.BuildServiceProvider();
        var tokenProvider = provider.GetRequiredService<ITokenProvider>();

        tokenProvider.Should().BeOfType<StaticTokenProvider>();
    }

    [Fact]
    public void AddLedgerClient_resolves_without_auth_for_unauthenticated_access()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GrpcAddress"] = "https://localhost:5001"
        }).Build();

        services.AddLedgerClient(config);

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ILedgerClient>();
        var tokenProvider = provider.GetRequiredService<ITokenProvider>();

        client.Should().NotBeNull();
        tokenProvider.Should().BeSameAs(ITokenProvider.None);
    }

    [Fact]
    public void AddAdminClient_resolves_without_auth_for_unauthenticated_access()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GrpcAddress"] = "https://localhost:5001"
        }).Build();

        services.AddAdminClient(config);

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IAdminClient>();
        var tokenProvider = provider.GetRequiredService<ITokenProvider>();

        client.Should().NotBeNull();
        tokenProvider.Should().BeSameAs(ITokenProvider.None);
    }

    [Fact]
    public void AddLedgerClient_resolves_with_action_overload_without_auth()
    {
        var services = new ServiceCollection();

        services.AddLedgerClient(o => o.GrpcAddress = "https://localhost:5001");

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ILedgerClient>();
        var tokenProvider = provider.GetRequiredService<ITokenProvider>();

        client.Should().NotBeNull();
        tokenProvider.Should().BeSameAs(ITokenProvider.None);
    }

    [Fact]
    public void AddAdminClient_resolves_with_action_overload_without_auth()
    {
        var services = new ServiceCollection();

        services.AddAdminClient(o => o.GrpcAddress = "https://localhost:5001");

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IAdminClient>();
        var tokenProvider = provider.GetRequiredService<ITokenProvider>();

        client.Should().NotBeNull();
        tokenProvider.Should().BeSameAs(ITokenProvider.None);
    }

    [Fact]
    public void AddCantonLedger_registers_ledger_and_admin_clients()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Canton:Ledger:GrpcAddress"] = "https://localhost:5001"
        }).Build();

        services.AddCantonLedger(config);

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ILedgerClient>().Should().BeOfType<LedgerClient>();
        provider.GetRequiredService<IAdminClient>().Should().BeOfType<AdminClient>();
    }

    [Fact]
    public void AddCantonLedger_binds_options_from_canton_ledger_section()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Canton:Ledger:GrpcAddress"] = "https://participant.example:5001",
            ["Canton:Ledger:UserId"] = "ledger-user"
        }).Build();

        services.AddCantonLedger(config);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LedgerClientOptions>>();
        options.Value.GrpcAddress.Should().Be("https://participant.example:5001");
        options.Value.UserId.Should().Be("ledger-user");
    }

    [Fact]
    public void AddCantonLedger_registers_client_credentials_when_canton_auth_populated()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Canton:Ledger:GrpcAddress"] = "https://localhost:5001",
            ["Canton:Auth:ClientId"] = "my-client",
            ["Canton:Auth:ClientSecret"] = "my-secret",
            ["Canton:Auth:Domain"] = "dev-peaceful.eu.auth0.com"
        }).Build();

        services.AddCantonLedger(config);

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITokenProvider>().Should().NotBeSameAs(ITokenProvider.None);

        var authOptions = provider.GetRequiredService<IOptions<ClientCredentialsOptions>>().Value;
        authOptions.ClientId.Should().Be("my-client");
        authOptions.ClientSecret.Should().Be("my-secret");
        authOptions.Domain.Should().Be("dev-peaceful.eu.auth0.com");
        authOptions.TokenGenerationEndpoint
            .Should().Be(new Uri("https://dev-peaceful.eu.auth0.com/oauth/token"));
    }

    [Fact]
    public void AddCantonLedger_skips_auth_when_canton_auth_section_absent()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Canton:Ledger:GrpcAddress"] = "https://localhost:5001"
        }).Build();

        services.AddCantonLedger(config);

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITokenProvider>().Should().BeSameAs(ITokenProvider.None);
    }

    [Fact]
    public void AddCantonLedger_skips_auth_when_canton_auth_values_are_whitespace()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Canton:Ledger:GrpcAddress"] = "https://localhost:5001",
            ["Canton:Auth:ClientId"] = "   "
        }).Build();

        services.AddCantonLedger(config);

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITokenProvider>().Should().BeSameAs(ITokenProvider.None);
    }

    [Fact]
    public void AddCantonLedger_fails_at_startup_when_auth_half_configured()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Canton:Ledger:GrpcAddress"] = "https://localhost:5001",
            ["Canton:Auth:ClientSecret"] = "my-secret",
            ["Canton:Auth:Domain"] = "dev-peaceful.eu.auth0.com"
        }).Build();

        services.AddCantonLedger(config);

        var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<ClientCredentialsOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void AddCantonLedger_skips_auth_options_binding_when_explicit_provider_registered()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Canton:Ledger:GrpcAddress"] = "https://localhost:5001",
            ["Canton:Auth:ClientSecret"] = "leftover-secret"
        }).Build();

        services.AddCantonStaticAuth("explicit-token");
        services.AddCantonLedger(config);

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITokenProvider>().Should().BeOfType<StaticTokenProvider>();
    }

    [Fact]
    public void AddCantonLedger_preserves_explicit_static_auth_registered_before()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Canton:Ledger:GrpcAddress"] = "https://localhost:5001",
            ["Canton:Auth:ClientId"] = "my-client",
            ["Canton:Auth:ClientSecret"] = "my-secret",
            ["Canton:Auth:Domain"] = "dev-peaceful.eu.auth0.com"
        }).Build();

        services.AddCantonStaticAuth("explicit-token");
        services.AddCantonLedger(config);

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITokenProvider>().Should().BeOfType<StaticTokenProvider>();
    }

    [Fact]
    public void AddCantonLedger_throws_for_null_services()
    {
        IServiceCollection services = null!;
        var config = new ConfigurationBuilder().Build();

        var act = () => services.AddCantonLedger(config);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddCantonLedger_throws_for_null_configuration()
    {
        var services = new ServiceCollection();

        var act = () => services.AddCantonLedger(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddCantonLedger_returns_services_for_chaining()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Canton:Ledger:GrpcAddress"] = "https://localhost:5001"
        }).Build();

        services.AddCantonLedger(config).Should().BeSameAs(services);
    }
}
