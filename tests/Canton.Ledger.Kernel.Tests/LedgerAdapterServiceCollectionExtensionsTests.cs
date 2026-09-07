// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.DependencyInjection;
using Daml.Ledger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public class LedgerAdapterServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLedgerAdapter_throws_when_the_service_collection_is_null()
    {
        var act = () => LedgerAdapterServiceCollectionExtensions.AddLedgerAdapter(
            null!, static _ => Substitute.For<ICantonLedgerClient>(), ServiceLifetime.Singleton);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddLedgerAdapter_throws_when_the_factory_is_null()
    {
        var act = () => new ServiceCollection().AddLedgerAdapter(
            (Func<IServiceProvider, ICantonLedgerClient>)null!, ServiceLifetime.Singleton);

        act.Should().Throw<ArgumentNullException>().WithParameterName("factory");
    }

    [Fact]
    public void AddLedgerAdapter_rejects_a_scoped_lifetime()
    {
        var act = () => new ServiceCollection().AddLedgerAdapter(
            static _ => Substitute.For<ICantonLedgerClient>(), ServiceLifetime.Scoped);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("lifetime");
    }

    [Fact]
    public void AddLedgerAdapter_serves_a_transient_adapter_to_every_neutral_service_type()
    {
        var services = new ServiceCollection();
        services.AddLedgerAdapter(static _ => Substitute.For<ICantonLedgerClient>(), ServiceLifetime.Transient);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ILedgerReader>().Should().NotBeNull();
        provider.GetRequiredService<ICantonLedgerClient>().Should()
            .NotBeSameAs(provider.GetRequiredService<ILedgerClient>());
    }

    [Fact]
    public void AddLedgerAdapter_serves_every_neutral_service_type_from_one_singleton_adapter()
    {
        var services = new ServiceCollection();
        services.AddLedgerAdapter(static _ => Substitute.For<ICantonLedgerClient>(), ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        var adapter = provider.GetRequiredService<ICantonLedgerClient>();

        provider.GetRequiredService<ILedgerClient>().Should().BeSameAs(adapter);
        provider.GetRequiredService<ILedgerReader>().Should().BeSameAs(adapter);
        provider.GetRequiredService<ILedgerWriter>().Should().BeSameAs(adapter);
        provider.GetRequiredService<ILedgerStreamer>().Should().BeSameAs(adapter);
    }

    [Fact]
    public void AddLedgerAdapter_keeps_a_service_type_the_consumer_registered_first()
    {
        var ownReader = Substitute.For<ILedgerReader>();
        var services = new ServiceCollection();
        services.AddSingleton(ownReader);

        services.AddLedgerAdapter(static _ => Substitute.For<ICantonLedgerClient>(), ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ILedgerReader>().Should().BeSameAs(ownReader);
    }
}
