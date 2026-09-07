// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client.Raw;
using Canton.Ledger.Kernel.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class GrpcCallInvokerFactoryTests
{
    private const string GrpcAddress = "https://localhost:5001";

    private static IConfiguration LedgerConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GrpcAddress"] = GrpcAddress
            })
            .Build();

    private static IConfiguration AuthConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Domain"] = "https://auth.example.com",
                ["ClientId"] = "my-client",
                ["ClientSecret"] = "my-secret"
            })
            .Build();

    [Fact]
    public void AddLedgerRawGrpc_registers_IGrpcCallInvokerFactory_as_singleton()
    {
        var services = new ServiceCollection();

        services.AddLedgerRawGrpc(LedgerConfiguration());

        var descriptor = services.Should()
            .ContainSingle(d => d.ServiceType == typeof(IGrpcCallInvokerFactory)).Subject;
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IGrpcCallInvokerFactory>().Should().BeOfType<GrpcCallInvokerFactory>();
    }

    [Fact]
    public void AddLedgerRawGrpc_registers_IGrpcCallInvokerFactory_from_a_configure_action()
    {
        var services = new ServiceCollection();

        services.AddLedgerRawGrpc(options => options.GrpcAddress = GrpcAddress);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IGrpcCallInvokerFactory>().Should().BeOfType<GrpcCallInvokerFactory>();
    }

    [Fact]
    public void AddLedgerRawGrpc_registers_an_ITokenProvider_from_the_auth_configuration()
    {
        var services = new ServiceCollection();

        services.AddLedgerRawGrpc(LedgerConfiguration(), AuthConfiguration());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITokenProvider>().Should().NotBeSameAs(ITokenProvider.None);
        provider.GetRequiredService<IGrpcCallInvokerFactory>().Should().BeOfType<GrpcCallInvokerFactory>();
    }

    [Fact]
    public void IGrpcCallInvokerFactory_is_absent_without_AddLedgerRawGrpc()
    {
        var services = new ServiceCollection();
        var configuration = LedgerConfiguration();

        services.AddLedgerClient(configuration);
        services.AddAdminClient(configuration);

        using var provider = services.BuildServiceProvider();
        provider.GetService<IGrpcCallInvokerFactory>().Should().BeNull();
    }

    [Fact]
    public void AddLedgerRawGrpc_does_not_register_the_typed_clients()
    {
        var services = new ServiceCollection();

        services.AddLedgerRawGrpc(LedgerConfiguration());

        using var provider = services.BuildServiceProvider();
        provider.GetService<ICantonLedgerClient>().Should().BeNull();
        provider.GetService<IAdminClient>().Should().BeNull();
    }

    [Fact]
    public void CreateCallInvoker_returns_the_authenticated_invoker()
    {
        var services = new ServiceCollection();
        services.AddLedgerRawGrpc(LedgerConfiguration());
        using var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<IGrpcCallInvokerFactory>();

        factory.CreateCallInvoker().Should().BeOfType<AuthenticatedCallInvoker>();
    }

    [Fact]
    public void CreateCallInvoker_throws_ObjectDisposedException_once_the_provider_is_disposed()
    {
        var services = new ServiceCollection();
        services.AddLedgerRawGrpc(LedgerConfiguration());
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IGrpcCallInvokerFactory>();

        provider.Dispose();

        var act = () => factory.CreateCallInvoker();

        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_does_not_throw_when_the_provider_is_disposed_twice()
    {
        var services = new ServiceCollection();
        services.AddLedgerRawGrpc(LedgerConfiguration());
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IGrpcCallInvokerFactory>();

        provider.Dispose();

        var act = provider.Dispose;

        act.Should().NotThrow();
    }
}
