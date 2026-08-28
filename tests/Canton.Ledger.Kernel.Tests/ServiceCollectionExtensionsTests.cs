// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Authentication.TokenGeneration;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public class ServiceCollectionExtensionsTests
{
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
    public void AddCantonStaticAuth_registers_static_provider()
    {
        var services = new ServiceCollection();

        services.AddCantonStaticAuth("my-static-token");

        var provider = services.BuildServiceProvider();
        var tokenProvider = provider.GetRequiredService<ITokenProvider>();
        tokenProvider.Should().BeOfType<StaticTokenProvider>();
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
}
