// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Authentication.TokenGeneration;
using Canton.Ledger.Kernel.Security;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public class ServiceCollectionExtensionsTests
{
    private const string FactoryToken = "factory-token";
    private const string InstanceToken = "instance-token";
    private const string KeyedTokenProviderKey = "keyed";
    private const string StaticToken = "my-static-token";

    [Fact]
    public void AddCantonAuth_registers_token_provider_as_singleton()
    {
        var services = new ServiceCollection();
        var config = BuildConfig();

        services.AddCantonAuth(config);

        var provider = services.BuildServiceProvider();
        var tokenProvider = provider.GetService<ITokenProvider>();
        tokenProvider.Should().NotBeNull();
        tokenProvider.Should().BeOfType<ClientCredentialsProvider>();
    }

    [Fact]
    public void AddCantonAuth_registers_unkeyed_ClientCredentialsProvider_after_keyed_ITokenProvider()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ITokenProvider>(KeyedTokenProviderKey, ITokenProvider.None);

        services.AddCantonAuth(BuildConfig());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITokenProvider>().Should().BeOfType<ClientCredentialsProvider>();
        provider.GetRequiredKeyedService<ITokenProvider>(KeyedTokenProviderKey).Should().BeSameAs(ITokenProvider.None);
    }

    [Fact]
    public void AddCantonAuth_replaces_exact_unkeyed_ITokenProvider_None_singleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(ITokenProvider.None);

        services.AddCantonAuth(BuildConfig());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITokenProvider>().Should().BeOfType<ClientCredentialsProvider>();
        services.Should().NotContain(descriptor => !descriptor.IsKeyedService
            && ReferenceEquals(descriptor.ImplementationInstance, ITokenProvider.None));
    }

    [Fact]
    public void AddCantonAuth_preserves_custom_ITokenProvider_descriptors_without_invoking_factory()
    {
        var factoryInvoked = false;
        var instanceDescriptor = ServiceDescriptor.Singleton<ITokenProvider>(
            new StaticTokenProvider(InstanceToken));
        var typeDescriptor = ServiceDescriptor.Scoped<ITokenProvider, TestTokenProvider>();
        var factoryDescriptor = ServiceDescriptor.Transient<ITokenProvider>(_ =>
        {
            factoryInvoked = true;
            return new StaticTokenProvider(FactoryToken);
        });
        IServiceCollection services = new ServiceCollection();
        services.Add(instanceDescriptor);
        services.Add(typeDescriptor);
        services.Add(factoryDescriptor);

        services.AddCantonAuth(BuildConfig());

        services.Should().Contain(instanceDescriptor);
        services.Should().Contain(typeDescriptor);
        services.Should().Contain(factoryDescriptor);
        factoryInvoked.Should().BeFalse();
    }

    [Fact]
    public void AddCantonAuth_binds_options_from_configuration()
    {
        var services = new ServiceCollection();
        var config = BuildConfig();

        services.AddCantonAuth(config);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ClientCredentialsOptions>>();
        options.Value.ClientId.Should().Be("my-client");
        options.Value.ClientSecret.Should().Be("my-secret");
        options.Value.Domain.Should().Be("https://auth.example.com");
    }

    [Fact]
    public void AddCantonAuth_binds_nested_tls_options_from_configuration()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ClientId"] = "my-client",
                ["ClientSecret"] = "my-secret",
                ["Domain"] = "https://auth.example.com",
                ["Tls:CertificateAuthorityBundlePemPath"] = "ca.pem",
                ["Tls:RevocationMode"] = "Online"
            })
            .Build();

        services.AddCantonAuth(config);

        using var provider = services.BuildServiceProvider();
        var tls = provider.GetRequiredService<IOptions<ClientCredentialsOptions>>().Value.Tls;

        tls.CertificateAuthorityBundlePemPath.Should().Be("ca.pem");
        tls.RevocationMode.Should().Be(X509RevocationMode.Online);
    }

    [Fact]
    public void AddCantonAuth_registers_http_client()
    {
        var services = new ServiceCollection();
        var config = BuildConfig();

        services.AddCantonAuth(config);

        var provider = services.BuildServiceProvider();
        var httpClientFactory = provider.GetService<IHttpClientFactory>();
        httpClientFactory.Should().NotBeNull();
    }

    [Fact]
    public void AddCantonAuth_bounds_CantonAuth_HttpClient_timeout_to_the_configured_TokenAcquisitionTimeout()
    {
        var services = new ServiceCollection();
        services.AddCantonAuth(opts =>
        {
            opts.ClientId = "my-client";
            opts.ClientSecret = "my-secret";
            opts.Domain = "https://auth.example.com";
            opts.TokenAcquisitionTimeout = TimeSpan.FromSeconds(5);
        });

        var provider = services.BuildServiceProvider();
        var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient("CantonAuth");

        httpClient.Timeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AddCantonAuth_defaults_CantonAuth_HttpClient_timeout_to_30_seconds()
    {
        var services = new ServiceCollection();
        services.AddCantonAuth(BuildConfig());

        var provider = services.BuildServiceProvider();
        var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient("CantonAuth");

        httpClient.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void AddCantonAuth_configured_tls_reaches_the_primary_handler()
    {
        using var key = ECDsa.Create();
        var request = new CertificateRequest(
            "CN=canton-auth-tls-tests", key, HashAlgorithmName.SHA256);
        using var authority = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var services = new ServiceCollection();
        services.AddCantonAuth(options =>
        {
            options.ClientId = "my-client";
            options.ClientSecret = "my-secret";
            options.Domain = "https://auth.example.com";
            options.Tls = new TlsOptions { CertificateAuthorities = [authority] };
        });

        using var provider = services.BuildServiceProvider();
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler("CantonAuth");
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler!;

        var socketsHandler = handler.Should().BeOfType<SocketsHttpHandler>().Subject;
        socketsHandler.SslOptions.CertificateChainPolicy!.CustomTrustStore
            .Should().ContainSingle().Which.Thumbprint.Should().Be(authority.Thumbprint);
    }

    [Fact]
    public void AddCantonAuth_fails_at_startup_when_nested_tls_is_incoherent()
    {
        var services = new ServiceCollection();
        services.AddCantonAuth(options =>
        {
            options.ClientId = "my-client";
            options.ClientSecret = "my-secret";
            options.Domain = "https://auth.example.com";
            options.Tls = new TlsOptions { ClientCertificatePkcs12Password = "unattached" };
        });
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<ClientCredentialsOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*ClientCertificatePkcs12Password*");
    }

    [Theory]
    [InlineData(nameof(ClientCredentialsOptions.TokenEndpoint))]
    [InlineData(nameof(ClientCredentialsOptions.Domain))]
    public void AddCantonAuth_fails_at_startup_when_Tls_is_configured_for_plaintext_effective_endpoint(
        string configuredEndpointMember)
    {
        var services = new ServiceCollection();
        services.AddCantonAuth(options =>
        {
            options.ClientId = "my-client";
            options.ClientSecret = "my-secret";
            if (configuredEndpointMember == nameof(ClientCredentialsOptions.TokenEndpoint))
                options.TokenEndpoint = new Uri("http://localhost:8080/oauth/token");
            else
                options.Domain = "http://localhost:8080";
            options.AllowInsecureTokenEndpoint = true;
            options.Tls = new TlsOptions { CertificateAuthorityBundlePemPath = "auth-ca.pem" };
        });
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<ClientCredentialsOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage(
                $"*ClientCredentialsOptions.Tls*ClientCredentialsOptions.{configuredEndpointMember}*plaintext http*https*");
    }

    [Fact]
    public void AddCantonStaticAuth_registers_static_provider()
    {
        var services = new ServiceCollection();

        services.AddCantonStaticAuth(StaticToken);

        var provider = services.BuildServiceProvider();
        var tokenProvider = provider.GetRequiredService<ITokenProvider>();
        tokenProvider.Should().BeOfType<StaticTokenProvider>();
    }

    [Fact]
    public void AddCantonStaticAuth_replaces_exact_unkeyed_ITokenProvider_None_and_preserves_keyed_registration()
    {
        var services = new ServiceCollection();
        services.AddSingleton(ITokenProvider.None);
        services.AddKeyedSingleton<ITokenProvider>(KeyedTokenProviderKey, ITokenProvider.None);

        services.AddCantonStaticAuth(StaticToken);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITokenProvider>().Should().BeOfType<StaticTokenProvider>();
        provider.GetRequiredKeyedService<ITokenProvider>(KeyedTokenProviderKey).Should().BeSameAs(ITokenProvider.None);
        services.Should().NotContain(descriptor => !descriptor.IsKeyedService
            && ReferenceEquals(descriptor.ImplementationInstance, ITokenProvider.None));
    }

    [Fact]
    public void AddCantonStaticAuth_preserves_custom_ITokenProvider_descriptors_without_invoking_factory()
    {
        var factoryInvoked = false;
        var instanceDescriptor = ServiceDescriptor.Singleton<ITokenProvider>(
            new StaticTokenProvider(InstanceToken));
        var typeDescriptor = ServiceDescriptor.Scoped<ITokenProvider, TestTokenProvider>();
        var factoryDescriptor = ServiceDescriptor.Transient<ITokenProvider>(_ =>
        {
            factoryInvoked = true;
            return new StaticTokenProvider(FactoryToken);
        });
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton(ITokenProvider.None);
        services.Add(instanceDescriptor);
        services.Add(typeDescriptor);
        services.Add(factoryDescriptor);

        services.AddCantonStaticAuth(StaticToken);

        services.Should().Contain(instanceDescriptor);
        services.Should().Contain(typeDescriptor);
        services.Should().Contain(factoryDescriptor);
        services.Should().HaveCount(3);
        factoryInvoked.Should().BeFalse();
    }

    [Fact]
    public void AddCantonAuth_with_action_configures_options()
    {
        var services = new ServiceCollection();

        services.AddCantonAuth(opts =>
        {
            opts.ClientId = "action-client";
            opts.ClientSecret = "action-secret";
            opts.Domain = "https://auth.example.com";
        });

        var provider = services.BuildServiceProvider();
        var tokenProvider = provider.GetService<ITokenProvider>();
        tokenProvider.Should().NotBeNull();
    }

    [Fact]
    public void AddCantonAuth_throws_OptionsValidationException_when_ITokenProvider_resolved_with_neither_Domain_nor_TokenEndpoint()
    {
        var services = new ServiceCollection();
        services.AddCantonAuth(opts =>
        {
            opts.ClientId = "action-client";
            opts.ClientSecret = "action-secret";
        });
        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<ITokenProvider>();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*Domain or TokenEndpoint*");
    }

    [Fact]
    public void AddCantonAuth_throws_OptionsValidationException_when_TokenEndpoint_is_plaintext_http_without_AllowInsecureTokenEndpoint()
    {
        var services = new ServiceCollection();
        services.AddCantonAuth(opts =>
        {
            opts.ClientId = "action-client";
            opts.ClientSecret = "action-secret";
            opts.TokenEndpoint = new Uri("http://idp.internal/oauth/token");
        });
        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<ITokenProvider>();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*plaintext http*AllowInsecureTokenEndpoint*");
    }

    [Fact]
    public async Task AddCantonAuth_delivers_ClientCredentialsProvider_logs_to_the_registered_ILoggerFactory()
    {
        var loggerFactory = new CapturingLoggerFactory();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddCantonAuth(BuildConfig());
        services.AddHttpClient("CantonAuth")
            .ConfigurePrimaryHttpMessageHandler(() =>
                new FakeHttpHandler().WithResponse(HttpStatusCode.Unauthorized, """{"error":"access_denied"}"""));

        var provider = services.BuildServiceProvider();
        var tokenProvider = provider.GetRequiredService<ITokenProvider>();

        var act = async () => await tokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<HttpRequestException>();

        loggerFactory.Records.Should().Contain(r =>
            r.Category == typeof(ClientCredentialsProvider).FullName
            && r.Level == LogLevel.Error
            && r.Message.Contains("Token acquisition failed"));
    }

    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ClientId"] = "my-client",
                ["ClientSecret"] = "my-secret",
                ["Domain"] = "https://auth.example.com",
                ["Audience"] = "https://canton.network/"
            })
            .Build();

    private sealed class TestTokenProvider : ITokenProvider
    {
        public TestTokenProvider()
        {
        }

        public Task<string> GetTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("test-token");
    }
}
