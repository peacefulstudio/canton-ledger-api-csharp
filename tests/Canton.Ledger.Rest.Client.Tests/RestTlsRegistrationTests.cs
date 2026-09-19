// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AwesomeAssertions;
using Canton.Ledger.Kernel.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestTlsRegistrationTests
{
    private static readonly TimeSpan HostHandlerLifetime = TimeSpan.FromMinutes(7);

    private static X509Certificate2 CertificateAuthority()
    {
        using var key = ECDsa.Create();
        var request = new CertificateRequest("CN=canton-rest-tls-tests", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static ServiceProvider BuildProvider(
        TlsOptions tls,
        Action<IServiceCollection>? before = null,
        Action<IServiceCollection>? after = null)
    {
        var services = new ServiceCollection();
        before?.Invoke(services);
        services.AddRestLedgerClient(options =>
        {
            options.HttpAddress = "http://ledger.example:7575";
            options.Tls = tls;
        });
        after?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static HttpMessageHandler PrimaryHandler(IServiceProvider provider)
    {
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(ServiceCollectionExtensions.HttpClientName);

        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler!;

        return handler;
    }

    private static int HandlerBuilderActionCount(IServiceProvider provider) =>
        provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
            .Get(ServiceCollectionExtensions.HttpClientName)
            .HttpMessageHandlerBuilderActions.Count;

    private static void AssertTrusts(SslClientAuthenticationOptions sslOptions, X509Certificate2 authority) =>
        sslOptions.CertificateChainPolicy!.CustomTrustStore
            .Should().ContainSingle().Which.Thumbprint.Should().Be(authority.Thumbprint);

    [Fact]
    public void AddRestLedgerClient_configured_tls_reaches_the_primary_handler()
    {
        using var authority = CertificateAuthority();
        using var provider = BuildProvider(new TlsOptions { CertificateAuthorities = [authority] });

        var handler = PrimaryHandler(provider).Should().BeOfType<SocketsHttpHandler>().Subject;

        AssertTrusts(handler.SslOptions, authority);
    }

    [Fact]
    public void AddRestLedgerClient_unconfigured_tls_registers_no_handler_builder_action()
    {
        using var authority = CertificateAuthority();
        using var configured = BuildProvider(new TlsOptions { CertificateAuthorities = [authority] });
        using var unconfigured = BuildProvider(new TlsOptions());

        HandlerBuilderActionCount(unconfigured).Should().Be(HandlerBuilderActionCount(configured) - 1);
    }

    [Fact]
    public void AddRestLedgerClient_host_primary_handler_wins_when_the_host_registers_first()
    {
        using var authority = CertificateAuthority();
        using var provider = BuildProvider(
            new TlsOptions { CertificateAuthorities = [authority] },
            before: static services => services
                .AddHttpClient(ServiceCollectionExtensions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
                {
                    SslOptions = new SslClientAuthenticationOptions { TargetHost = "chosen-by-the-host" }
                }));

        var handler = PrimaryHandler(provider).Should().BeOfType<SocketsHttpHandler>().Subject;

        handler.SslOptions.TargetHost.Should().Be("chosen-by-the-host");
        handler.SslOptions.CertificateChainPolicy.Should().BeNull();
    }

    [Fact]
    public void AddRestLedgerClient_host_primary_handler_wins_when_the_host_registers_last()
    {
        using var authority = CertificateAuthority();
        using var provider = BuildProvider(
            new TlsOptions { CertificateAuthorities = [authority] },
            after: static services => services
                .AddHttpClient(ServiceCollectionExtensions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
                {
                    SslOptions = new SslClientAuthenticationOptions { TargetHost = "chosen-by-the-host" }
                }));

        var handler = PrimaryHandler(provider).Should().BeOfType<SocketsHttpHandler>().Subject;

        handler.SslOptions.TargetHost.Should().Be("chosen-by-the-host");
        handler.SslOptions.CertificateChainPolicy.Should().BeNull();
    }

    [Fact]
    public void AddRestLedgerClient_UseSocketsHttpHandler_for_an_unrelated_concern_keeps_the_typed_tls()
    {
        using var authority = CertificateAuthority();
        using var provider = BuildProvider(
            new TlsOptions { CertificateAuthorities = [authority] },
            after: static services => services
                .AddHttpClient(ServiceCollectionExtensions.HttpClientName)
                .UseSocketsHttpHandler(static (handler, _) => handler.MaxConnectionsPerServer = 42));

        var handler = PrimaryHandler(provider).Should().BeOfType<SocketsHttpHandler>().Subject;

        handler.MaxConnectionsPerServer.Should().Be(42);
        AssertTrusts(handler.SslOptions, authority);
    }

    [Fact]
    public void AddRestLedgerClient_handler_built_over_a_foreign_primary_handler_carries_the_factory_handler_lifetime()
    {
        using var authority = CertificateAuthority();
        using var provider = BuildProvider(
            new TlsOptions { CertificateAuthorities = [authority] },
            after: static services =>
            {
                services.AddHttpClient(ServiceCollectionExtensions.HttpClientName)
                    .SetHandlerLifetime(HostHandlerLifetime);
                services.PostConfigure<HttpClientFactoryOptions>(
                    ServiceCollectionExtensions.HttpClientName,
                    static options => options.HttpMessageHandlerBuilderActions.Insert(
                        0, static builder => builder.PrimaryHandler = new HttpClientHandler()));
            });

        var handler = PrimaryHandler(provider).Should().BeOfType<SocketsHttpHandler>().Subject;

        handler.PooledConnectionLifetime.Should().Be(HostHandlerLifetime);
        AssertTrusts(handler.SslOptions, authority);
    }

    [Fact]
    public void AddRestLedgerClient_socket_handler_already_in_place_is_reused_rather_than_replaced()
    {
        using var authority = CertificateAuthority();
        using var provider = BuildProvider(
            new TlsOptions { CertificateAuthorities = [authority] },
            after: static services => services.PostConfigure<HttpClientFactoryOptions>(
                ServiceCollectionExtensions.HttpClientName,
                static options => options.HttpMessageHandlerBuilderActions.Insert(
                    0, static builder => builder.PrimaryHandler = new SocketsHttpHandler
                    {
                        MaxConnectionsPerServer = 17
                    })));

        var handler = PrimaryHandler(provider).Should().BeOfType<SocketsHttpHandler>().Subject;

        handler.MaxConnectionsPerServer.Should().Be(17);
        AssertTrusts(handler.SslOptions, authority);
    }

    [Fact]
    public void AddRestLedgerClient_configuration_registered_client_binds_the_tls_section_onto_its_handler()
    {
        using var authority = CertificateAuthority();
        var bundlePath = Path.Combine(Path.GetTempPath(), $"canton-rest-tls-{Guid.NewGuid():N}.pem");
        File.WriteAllText(bundlePath, authority.ExportCertificatePem());

        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Canton:Rest:HttpAddress"] = "https://ledger.example:7575",
                ["Canton:Rest:Tls:CertificateAuthorityBundlePemPath"] = bundlePath,
                ["Canton:Rest:Tls:RevocationMode"] = "Online"
            }).Build();

            var services = new ServiceCollection();
            services.AddRestLedgerClient(configuration.GetSection("Canton:Rest"));
            using var provider = services.BuildServiceProvider();

            var bound = provider.GetRequiredService<IOptions<RestLedgerClientOptions>>().Value.Tls;
            bound.CertificateAuthorityBundlePemPath.Should().Be(bundlePath);
            bound.RevocationMode.Should().Be(X509RevocationMode.Online);

            var handler = PrimaryHandler(provider).Should().BeOfType<SocketsHttpHandler>().Subject;

            AssertTrusts(handler.SslOptions, authority);
            handler.SslOptions.CertificateChainPolicy!.RevocationMode
                .Should().Be(X509RevocationMode.Online);
        }
        finally
        {
            File.Delete(bundlePath);
        }
    }

    [Fact]
    public void AddRestLedgerClient_fails_at_startup_when_the_tls_material_is_incoherent()
    {
        using var provider = BuildProvider(new TlsOptions { ClientCertificatePkcs12Password = "unattached" });

        var act = () => provider.GetRequiredService<IOptions<RestLedgerClientOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*ClientCertificatePkcs12Password*");
    }
}
