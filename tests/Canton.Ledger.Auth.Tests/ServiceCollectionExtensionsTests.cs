// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Canton.Ledger.Auth.TokenGeneration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Canton.Ledger.Auth.Tests;

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
