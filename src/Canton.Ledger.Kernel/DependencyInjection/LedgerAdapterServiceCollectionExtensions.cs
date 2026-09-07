// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Canton.Ledger.Kernel.DependencyInjection;

/// <summary>
/// The single answer to "which service types does a ledger adapter expose". Every transport
/// registers its adapter through <see cref="AddLedgerAdapter{TAdapter}"/>, so the transport-neutral
/// surface cannot drift between them and a new neutral interface is added in one place.
/// </summary>
internal static class LedgerAdapterServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TAdapter"/> and the whole transport-neutral surface it serves —
    /// <see cref="ICantonLedgerClient"/>, <see cref="ILedgerClient"/>, <see cref="ILedgerReader"/>,
    /// <see cref="ILedgerWriter"/> and <see cref="ILedgerStreamer"/> — at <paramref name="lifetime"/>.
    /// Each interface forwards to the one <typeparamref name="TAdapter"/> resolution, so under
    /// <see cref="ServiceLifetime.Singleton"/> all six service types hand back the same instance and
    /// the adapter's transport connection is shared.
    /// </summary>
    /// <remarks>
    /// Every registration is a <c>TryAdd</c>, so a consumer who registered their own implementation of
    /// any of these service types before calling a transport's <c>Add*</c> method keeps it.
    /// </remarks>
    /// <typeparam name="TAdapter">The concrete adapter type serving the participant.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="factory">Creates the adapter from the resolved services it depends on.</param>
    /// <param name="lifetime">
    /// The lifetime shared by the adapter and every service type forwarding to it. Only
    /// <see cref="ServiceLifetime.Singleton"/> and <see cref="ServiceLifetime.Transient"/> are supported.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lifetime"/> is <see cref="ServiceLifetime.Scoped"/>.</exception>
    /// <returns>The service collection for chaining.</returns>
    internal static IServiceCollection AddLedgerAdapter<TAdapter>(
        this IServiceCollection services,
        Func<IServiceProvider, TAdapter> factory,
        ServiceLifetime lifetime)
        where TAdapter : class, ICantonLedgerClient
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        if (lifetime is not (ServiceLifetime.Singleton or ServiceLifetime.Transient))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifetime),
                lifetime,
                "A ledger adapter is registered as Singleton, sharing one transport connection, or as "
                + "Transient, a wrapper per resolution. A scoped adapter would open a transport "
                + "connection per scope and outlive none of them predictably.");
        }

        services.TryAdd(ServiceDescriptor.Describe(typeof(TAdapter), factory, lifetime));
        ForwardToAdapter<TAdapter, ICantonLedgerClient>(services, lifetime);
        ForwardToAdapter<TAdapter, ILedgerClient>(services, lifetime);
        ForwardToAdapter<TAdapter, ILedgerReader>(services, lifetime);
        ForwardToAdapter<TAdapter, ILedgerWriter>(services, lifetime);
        ForwardToAdapter<TAdapter, ILedgerStreamer>(services, lifetime);

        return services;
    }

    private static void ForwardToAdapter<TAdapter, TService>(IServiceCollection services, ServiceLifetime lifetime)
        where TAdapter : class, TService
        where TService : class =>
        services.TryAdd(ServiceDescriptor.Describe(
            typeof(TService),
            static provider => provider.GetRequiredService<TAdapter>(),
            lifetime));
}
