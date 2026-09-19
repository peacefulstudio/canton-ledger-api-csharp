// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Authentication.TokenGeneration;
using Canton.Ledger.Kernel.Resilience;
using Canton.Ledger.Kernel.Security;
using Daml.Ledger.Abstractions;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
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

        var ledgerDescriptor = services.Should().ContainSingle(d => d.ServiceType == typeof(ILedgerClient)).Subject;
        ledgerDescriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);

        var cantonDescriptor = services.Should().ContainSingle(d => d.ServiceType == typeof(ICantonLedgerClient)).Subject;
        cantonDescriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICantonLedgerClient>().Should().BeOfType<LedgerClient>();
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

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IAdminClient>().Should().BeOfType<AdminClient>();
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
    public async Task AddCantonStaticAuth_replaces_unauthenticated_fallback_registered_by_AddLedgerClient()
    {
        const string grpcAddress = "https://localhost:5001";
        const string staticToken = "static-token";
        var services = new ServiceCollection();
        services.AddLedgerClient(options => options.GrpcAddress = grpcAddress);

        services.AddCantonStaticAuth(staticToken);

        using var provider = services.BuildServiceProvider();
        var token = await provider.GetRequiredService<ITokenProvider>()
            .GetTokenAsync(TestContext.Current.CancellationToken);
        token.Should().Be(staticToken);
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

    private static ServiceCollection ServicesWithCapturingLogging(CapturingLoggerFactory loggerFactory)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        return services;
    }

    [Fact]
    public void AddLedgerClient_without_auth_delivers_LedgerClient_logs_to_the_registered_ILoggerFactory()
    {
        var loggerFactory = new CapturingLoggerFactory();
        var services = ServicesWithCapturingLogging(loggerFactory);

        services.AddLedgerClient(o => o.GrpcAddress = "https://localhost:5001");
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ILedgerClient>();

        loggerFactory.Records.Should().Contain(r =>
            r.Category == typeof(LedgerClient).FullName
            && r.Level == LogLevel.Information
            && r.Message.Contains("LedgerClient initialized"));
    }

    [Fact]
    public void AddLedgerClient_without_auth_emits_the_unauthenticated_mode_warning()
    {
        var loggerFactory = new CapturingLoggerFactory();
        var services = ServicesWithCapturingLogging(loggerFactory);

        services.AddLedgerClient(o => o.GrpcAddress = "https://localhost:5001");
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ILedgerClient>();

        loggerFactory.Records.Should().Contain(r =>
            r.Category == typeof(LedgerClient).FullName
            && r.Level == LogLevel.Warning
            && r.Message.Contains("unauthenticated mode"));
    }

    [Fact]
    public void AddAdminClient_without_auth_emits_the_unauthenticated_mode_warning()
    {
        var loggerFactory = new CapturingLoggerFactory();
        var services = ServicesWithCapturingLogging(loggerFactory);

        services.AddAdminClient(o => o.GrpcAddress = "https://localhost:5001");
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IAdminClient>();

        loggerFactory.Records.Should().Contain(r =>
            r.Category == typeof(AdminClient).FullName
            && r.Level == LogLevel.Warning
            && r.Message.Contains("unauthenticated mode"));
    }

    [Fact]
    public void AddLedgerClient_with_auth_delivers_LedgerClient_logs_to_the_registered_ILoggerFactory()
    {
        var loggerFactory = new CapturingLoggerFactory();
        var services = ServicesWithCapturingLogging(loggerFactory);

        services.AddCantonStaticAuth("test-token");
        services.AddLedgerClient(o => o.GrpcAddress = "https://localhost:5001");
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ILedgerClient>();

        loggerFactory.Records.Should().Contain(r =>
            r.Category == typeof(LedgerClient).FullName
            && r.Level == LogLevel.Information
            && r.Message.Contains("LedgerClient initialized"));
        loggerFactory.Records.Should().NotContain(r => r.Level == LogLevel.Warning);
    }

    [Fact]
    public void AddLedgerClient_resolves_ICantonLedgerClient_as_the_same_instance_as_ILedgerClient()
    {
        var services = new ServiceCollection();

        services.AddLedgerClient(o => o.GrpcAddress = "https://localhost:5001");

        var provider = services.BuildServiceProvider();
        var cantonClient = provider.GetRequiredService<ICantonLedgerClient>();
        var ledgerClient = provider.GetRequiredService<ILedgerClient>();

        cantonClient.Should().BeOfType<LedgerClient>();
        cantonClient.Should().BeSameAs(ledgerClient);
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
    public void AddCantonLedger_resolves_ICantonLedgerClient_as_the_same_instance_as_ILedgerClient()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Canton:Ledger:GrpcAddress"] = "https://localhost:5001"
        }).Build();

        services.AddCantonLedger(config);

        var provider = services.BuildServiceProvider();
        var cantonClient = provider.GetRequiredService<ICantonLedgerClient>();
        cantonClient.Should().BeOfType<LedgerClient>();
        cantonClient.Should().BeSameAs(provider.GetRequiredService<ILedgerClient>());
    }

    [Fact]
    public async Task Provider_DisposeAsync_disposes_the_ledger_client_without_throwing()
    {
        var services = new ServiceCollection();
        services.AddLedgerClient(o => o.GrpcAddress = "https://localhost:5001");
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICantonLedgerClient>();
        provider.GetRequiredService<ILedgerClient>();

        var action = async () => await provider.DisposeAsync();

        await action.Should().NotThrowAsync(
            "the container captures the single client instance under both service types, so async disposal must tolerate being invoked more than once");
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
            ["Canton:Auth:Domain"] = "auth.example.com"
        }).Build();

        services.AddCantonLedger(config);

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITokenProvider>().Should().NotBeSameAs(ITokenProvider.None);

        var authOptions = provider.GetRequiredService<IOptions<ClientCredentialsOptions>>().Value;
        authOptions.ClientId.Should().Be("my-client");
        authOptions.ClientSecret.Should().Be("my-secret");
        authOptions.Domain.Should().Be("auth.example.com");
        authOptions.TokenGenerationEndpoint
            .Should().Be(new Uri("https://auth.example.com/oauth/token"));
    }

    [Fact]
    public void AddCantonLedger_replaces_unauthenticated_fallback_registered_by_AddLedgerClient()
    {
        var services = new ServiceCollection();
        services.AddLedgerClient(options => options.GrpcAddress = "https://localhost:5001");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Canton:Ledger:GrpcAddress"] = "https://localhost:5001",
            ["Canton:Auth:ClientId"] = "my-client",
            ["Canton:Auth:ClientSecret"] = "my-secret",
            ["Canton:Auth:Domain"] = "auth.example.com"
        }).Build();

        services.AddCantonLedger(config);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITokenProvider>().Should().BeOfType<ClientCredentialsProvider>();
    }

    [Fact]
    public void AddCantonLedger_registers_unkeyed_client_credentials_when_only_keyed_ITokenProvider_exists()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ITokenProvider>("keyed", ITokenProvider.None);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Canton:Ledger:GrpcAddress"] = "https://localhost:5001",
            ["Canton:Auth:ClientId"] = "my-client",
            ["Canton:Auth:ClientSecret"] = "my-secret",
            ["Canton:Auth:Domain"] = "auth.example.com"
        }).Build();

        services.AddCantonLedger(config);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITokenProvider>().Should().BeOfType<ClientCredentialsProvider>();
        provider.GetRequiredKeyedService<ITokenProvider>("keyed").Should().BeSameAs(ITokenProvider.None);
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
            ["Canton:Auth:Domain"] = "auth.example.com"
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
            ["Canton:Auth:Domain"] = "auth.example.com"
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

    [Fact]
    public void AddLedgerClient_fails_at_startup_when_Retry_MaxRetryAttempts_negative()
    {
        var services = new ServiceCollection();
        services.AddLedgerClient(options =>
        {
            options.GrpcAddress = "https://localhost:5001";
            options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = -1 };
        });
        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<LedgerClientOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*MaxRetryAttempts*");
    }

    [Fact]
    public void AddLedgerClient_fails_at_startup_when_Retry_Delay_negative()
    {
        var services = new ServiceCollection();
        services.AddLedgerClient(options =>
        {
            options.GrpcAddress = "https://localhost:5001";
            options.Retry = new RetryOptions { Enabled = true, Delay = TimeSpan.FromMilliseconds(-1) };
        });
        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<LedgerClientOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*Delay*");
    }

    [Fact]
    public void AddLedgerClient_starts_when_Retry_configured_with_nonnegative_values()
    {
        var services = new ServiceCollection();
        services.AddLedgerClient(options =>
        {
            options.GrpcAddress = "https://localhost:5001";
            options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 5, Delay = TimeSpan.FromMilliseconds(200) };
        });
        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<LedgerClientOptions>>().Value;

        act.Should().NotThrow();
    }

    [Fact]
    public void AddLedgerClient_fails_at_startup_when_ledger_tls_is_configured_and_auth_tls_is_not()
    {
        var services = new ServiceCollection();
        services.AddCantonAuth(options =>
        {
            options.ClientId = "client";
            options.ClientSecret = "secret";
            options.Domain = "https://auth.example.com";
        });
        services.AddLedgerClient(options =>
        {
            options.GrpcAddress = "https://localhost:5001";
            options.Tls = new TlsOptions { ClientCertificatePemPath = "ledger-client.pem" };
        });
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<LedgerClientOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*ClientCredentialsOptions.Tls*");
    }

    [Fact]
    public void AddLedgerClient_starts_when_ledger_tls_and_auth_tls_are_configured()
    {
        var services = new ServiceCollection();
        services.AddCantonAuth(options =>
        {
            options.ClientId = "client";
            options.ClientSecret = "secret";
            options.Domain = "https://auth.example.com";
            options.Tls = new TlsOptions { ClientCertificatePemPath = "auth-client.pem" };
        });
        services.AddLedgerClient(options =>
        {
            options.GrpcAddress = "https://localhost:5001";
            options.Tls = new TlsOptions { ClientCertificatePemPath = "ledger-client.pem" };
        });
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<LedgerClientOptions>>().Value;

        act.Should().NotThrow();
    }

    [Fact]
    public void AddLedgerClient_starts_when_ledger_tls_is_configured_with_static_auth()
    {
        var services = new ServiceCollection();
        services.AddCantonStaticAuth("static-token");
        services.AddLedgerClient(options =>
        {
            options.GrpcAddress = "https://localhost:5001";
            options.Tls = new TlsOptions { ClientCertificatePemPath = "ledger-client.pem" };
        });
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<LedgerClientOptions>>().Value;

        act.Should().NotThrow();
    }

    [Fact]
    public void AddLedgerClient_options_validation_does_not_resolve_preexisting_scoped_ITokenProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITokenProvider>(static _ =>
            throw new InvalidOperationException("The scoped token provider must not be resolved."));
        services.AddCantonAuth(options =>
        {
            options.ClientId = "client";
            options.ClientSecret = "secret";
            options.Domain = "https://auth.example.com";
        });
        services.AddLedgerClient(options =>
        {
            options.GrpcAddress = "https://localhost:5001";
            options.Tls = new TlsOptions { ClientCertificatePemPath = "ledger-client.pem" };
        });
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var act = () => provider.GetRequiredService<IOptions<LedgerClientOptions>>().Value;

        act.Should().NotThrow();
    }

    [Fact]
    public void AddLedgerClient_starts_with_ledger_tls_when_preexisting_ITokenProvider_wins_over_AddCantonAuth()
    {
        var services = new ServiceCollection();
        services.AddCantonStaticAuth("static-token");
        services.AddCantonAuth(options =>
        {
            options.ClientId = "client";
            options.ClientSecret = "secret";
            options.Domain = "https://auth.example.com";
        });
        services.AddLedgerClient(options =>
        {
            options.GrpcAddress = "https://localhost:5001";
            options.Tls = new TlsOptions { ClientCertificatePemPath = "ledger-client.pem" };
        });
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ITokenProvider>().Should().BeOfType<StaticTokenProvider>();
        var act = () => provider.GetRequiredService<IOptions<LedgerClientOptions>>().Value;

        act.Should().NotThrow();
    }

    [Fact]
    public void AddLedgerClient_starts_when_ledger_tls_and_auth_tls_are_unconfigured()
    {
        var services = new ServiceCollection();
        services.AddCantonAuth(options =>
        {
            options.ClientId = "client";
            options.ClientSecret = "secret";
            options.Domain = "https://auth.example.com";
        });
        services.AddLedgerClient(options => options.GrpcAddress = "https://localhost:5001");
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<LedgerClientOptions>>().Value;

        act.Should().NotThrow();
    }

    [Fact]
    public void AddLedgerClient_fails_at_startup_when_ledger_tls_is_configured_and_auth_tls_is_not_reversed_order()
    {
        var services = new ServiceCollection();
        services.AddLedgerClient(options =>
        {
            options.GrpcAddress = "https://localhost:5001";
            options.Tls = new TlsOptions { ClientCertificatePemPath = "ledger-client.pem" };
        });
        services.AddCantonAuth(options =>
        {
            options.ClientId = "client";
            options.ClientSecret = "secret";
            options.Domain = "https://auth.example.com";
        });
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ITokenProvider>().Should().BeOfType<ClientCredentialsProvider>();
        var act = () => provider.GetRequiredService<IOptions<LedgerClientOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*ClientCredentialsOptions.Tls*");
    }

    [Theory]
    [InlineData(typeof(ILedgerReader))]
    [InlineData(typeof(ILedgerWriter))]
    [InlineData(typeof(ILedgerStreamer))]
    [InlineData(typeof(ILedgerClient))]
    [InlineData(typeof(ICantonLedgerClient))]
    public void AddLedgerClient_resolves_the_transport_neutral_surface_as_the_one_LedgerClient(Type serviceType)
    {
        var services = new ServiceCollection();
        services.AddLedgerClient(options => options.GrpcAddress = "https://localhost:5001");
        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService(serviceType);

        resolved.Should().BeOfType<LedgerClient>();
        resolved.Should().BeSameAs(provider.GetRequiredService<ICantonLedgerClient>());
    }

    [Theory]
    [InlineData(typeof(ILedgerReader))]
    [InlineData(typeof(ILedgerWriter))]
    [InlineData(typeof(ILedgerStreamer))]
    [InlineData(typeof(ILedgerClient))]
    [InlineData(typeof(ICantonLedgerClient))]
    public void AddLedgerClient_registers_the_transport_neutral_surface_as_singleton(Type serviceType)
    {
        var services = new ServiceCollection();
        services.AddLedgerClient(options => options.GrpcAddress = "https://localhost:5001");

        services.Should().ContainSingle(descriptor => descriptor.ServiceType == serviceType)
            .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Theory]
    [InlineData(typeof(ILedgerReader))]
    [InlineData(typeof(ILedgerWriter))]
    [InlineData(typeof(ILedgerStreamer))]
    [InlineData(typeof(ILedgerClient))]
    [InlineData(typeof(ICantonLedgerClient))]
    public void AddCantonLedger_resolves_the_transport_neutral_surface_as_the_one_LedgerClient(Type serviceType)
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Canton:Ledger:GrpcAddress"] = "https://localhost:5001"
            })
            .Build();
        services.AddCantonLedger(config);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService(serviceType).Should().BeSameAs(provider.GetRequiredService<ICantonLedgerClient>());
    }

    [Fact]
    public void AddLedgerClient_keeps_a_consumer_registration_made_before_it()
    {
        var services = new ServiceCollection();
        var preregistered = Substitute.For<ILedgerReader>();
        services.AddSingleton<ILedgerReader>(preregistered);

        services.AddLedgerClient(options => options.GrpcAddress = "https://localhost:5001");
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ILedgerReader>().Should().BeSameAs(preregistered);
    }
}
