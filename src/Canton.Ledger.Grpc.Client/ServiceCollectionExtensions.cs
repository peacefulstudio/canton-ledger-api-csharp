// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client.Raw;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.DependencyInjection;
using Daml.Ledger.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// Extension methods for registering Canton Ledger API clients with the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Convention-based registration for the ledger client and <see cref="IAdminClient"/> using canonical
    /// <c>Canton:Ledger</c> and <c>Canton:Auth</c> configuration sections. The ledger client is a
    /// <see cref="ServiceLifetime.Singleton"/> resolvable as <see cref="ICantonLedgerClient"/>,
    /// <see cref="ILedgerClient"/>, <see cref="ILedgerReader"/>, <see cref="ILedgerWriter"/> and
    /// <see cref="ILedgerStreamer"/> — all five hand back the same instance, so the gRPC channel is shared.
    /// </summary>
    /// <remarks>
    /// Reads <c>Canton:Ledger</c> for <see cref="LedgerClientOptions"/>. Auth registration
    /// is triggered when the <c>Canton:Auth</c> section has any populated child value: a
    /// client-credentials <see cref="ITokenProvider"/> is registered and its options are
    /// validated at startup, so half-configured auth (e.g. <c>ClientSecret</c> set without
    /// <c>ClientId</c>) fails loudly instead of silently falling back to unauthenticated.
    /// When no auth values are present the clients run unauthenticated via
    /// <see cref="ITokenProvider.None"/>. Any pre-existing <see cref="ITokenProvider"/>
    /// registration (e.g. from <c>AddCantonStaticAuth</c>) wins and suppresses
    /// <c>Canton:Auth</c> binding entirely, so leftover auth config cannot fail startup
    /// when an explicit provider has been chosen. Prefer this over the per-client overloads
    /// so consumers and their deployment config (env vars, Helm charts, appsettings) agree
    /// on a single canonical wiring.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The root configuration (sections read by convention).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCantonLedger(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var auth = configuration.GetSection("Canton:Auth");
        if (HasAnyConfiguredValue(auth) && !services.Any(d => d.ServiceType == typeof(ITokenProvider)))
            services.AddCantonAuth(auth);

        AddLedgerOptions(services, configuration.GetSection("Canton:Ledger"));
        AddLedgerClientRegistration(services);
        AddAdminClientRegistration(services);

        return services;
    }

    private static bool HasAnyConfiguredValue(IConfiguration section)
    {
        if (section is IConfigurationSection { Value: { } value } && !string.IsNullOrWhiteSpace(value))
            return true;

        foreach (var child in section.GetChildren())
        {
            if (HasAnyConfiguredValue(child))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Registers the ledger client as a <see cref="ServiceLifetime.Singleton"/> resolvable as
    /// <see cref="ICantonLedgerClient"/>, <see cref="ILedgerClient"/>, <see cref="ILedgerReader"/>,
    /// <see cref="ILedgerWriter"/> and <see cref="ILedgerStreamer"/> — all five hand back the same
    /// instance, so the gRPC channel is shared — and binds <see cref="LedgerClientOptions"/>
    /// from the provided configuration section. Options are validated at startup.
    /// </summary>
    /// <remarks>
    /// Calling both <see cref="AddLedgerClient(IServiceCollection, IConfiguration)"/> and
    /// <see cref="AddAdminClient(IServiceCollection, IConfiguration)"/> with the same configuration
    /// section is safe — both share <see cref="LedgerClientOptions"/> and the binding delegates
    /// will produce the same result.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// A configuration section containing <see cref="LedgerClientOptions"/> values
    /// (e.g., <c>configuration.GetSection("Canton:Ledger")</c>).
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLedgerClient(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AddLedgerOptions(services, configuration);
        AddLedgerClientRegistration(services);

        return services;
    }

    /// <summary>
    /// Registers the ledger client as a <see cref="ServiceLifetime.Singleton"/> resolvable as
    /// <see cref="ICantonLedgerClient"/>, <see cref="ILedgerClient"/>, <see cref="ILedgerReader"/>,
    /// <see cref="ILedgerWriter"/> and <see cref="ILedgerStreamer"/> — all five hand back the same
    /// instance, so the gRPC channel is shared — and configures <see cref="LedgerClientOptions"/>
    /// using the provided action delegate. Options are validated at startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure <see cref="LedgerClientOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLedgerClient(this IServiceCollection services, Action<LedgerClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        AddLedgerOptions(services, configure);
        AddLedgerClientRegistration(services);

        return services;
    }

    /// <summary>
    /// Registers the ledger client as a <see cref="ServiceLifetime.Singleton"/> resolvable as
    /// <see cref="ICantonLedgerClient"/>, <see cref="ILedgerClient"/>, <see cref="ILedgerReader"/>,
    /// <see cref="ILedgerWriter"/> and <see cref="ILedgerStreamer"/> — all five hand back the same
    /// instance, so the gRPC channel is shared — binds <see cref="LedgerClientOptions"/>
    /// from the provided configuration section, and auto-registers <see cref="ITokenProvider"/> as a
    /// <see cref="Canton.Ledger.Kernel.Authentication.TokenGeneration.ClientCredentialsProvider"/> from the auth configuration section.
    /// If an <see cref="ITokenProvider"/> is already registered, the existing registration is kept.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// A configuration section containing <see cref="LedgerClientOptions"/> values
    /// (e.g., <c>configuration.GetSection("Canton:Ledger")</c>).
    /// </param>
    /// <param name="authConfiguration">
    /// A configuration section containing <see cref="Canton.Ledger.Kernel.Authentication.TokenGeneration.ClientCredentialsOptions"/> values
    /// (e.g., <c>configuration.GetSection("Canton:Auth")</c>).
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLedgerClient(
        this IServiceCollection services,
        IConfiguration configuration,
        IConfiguration authConfiguration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(authConfiguration);

        services.AddCantonAuth(authConfiguration);
        AddLedgerOptions(services, configuration);
        AddLedgerClientRegistration(services);

        return services;
    }

    /// <summary>
    /// Registers <see cref="IAdminClient"/> as a singleton and binds <see cref="LedgerClientOptions"/>
    /// from the provided configuration section. Options are validated at startup.
    /// </summary>
    /// <remarks>
    /// Calling both <see cref="AddLedgerClient(IServiceCollection, IConfiguration)"/> and
    /// <see cref="AddAdminClient(IServiceCollection, IConfiguration)"/> with the same configuration
    /// section is safe — both share <see cref="LedgerClientOptions"/> and the binding delegates
    /// will produce the same result.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// A configuration section containing <see cref="LedgerClientOptions"/> values
    /// (e.g., <c>configuration.GetSection("Canton:Ledger")</c>).
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAdminClient(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AddLedgerOptions(services, configuration);
        AddAdminClientRegistration(services);

        return services;
    }

    /// <summary>
    /// Registers <see cref="IAdminClient"/> as a singleton, binds <see cref="LedgerClientOptions"/>
    /// from the provided configuration section, and auto-registers <see cref="ITokenProvider"/> as a
    /// <see cref="Canton.Ledger.Kernel.Authentication.TokenGeneration.ClientCredentialsProvider"/> from the auth configuration section.
    /// If an <see cref="ITokenProvider"/> is already registered, the existing registration is kept.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// A configuration section containing <see cref="LedgerClientOptions"/> values
    /// (e.g., <c>configuration.GetSection("Canton:Ledger")</c>).
    /// </param>
    /// <param name="authConfiguration">
    /// A configuration section containing <see cref="Canton.Ledger.Kernel.Authentication.TokenGeneration.ClientCredentialsOptions"/> values
    /// (e.g., <c>configuration.GetSection("Canton:Auth")</c>).
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAdminClient(
        this IServiceCollection services,
        IConfiguration configuration,
        IConfiguration authConfiguration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(authConfiguration);

        services.AddCantonAuth(authConfiguration);
        AddLedgerOptions(services, configuration);
        AddAdminClientRegistration(services);

        return services;
    }

    /// <summary>
    /// Registers <see cref="IAdminClient"/> as a singleton and configures <see cref="LedgerClientOptions"/>
    /// using the provided action delegate. Options are validated at startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure <see cref="LedgerClientOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAdminClient(this IServiceCollection services, Action<LedgerClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        AddLedgerOptions(services, configure);
        AddAdminClientRegistration(services);

        return services;
    }

    /// <summary>
    /// Registers <see cref="IGrpcCallInvokerFactory"/> from <c>Canton.Ledger.Grpc.Client.Raw</c> as a
    /// <see cref="ServiceLifetime.Singleton"/> and binds <see cref="LedgerClientOptions"/> from the
    /// provided configuration section. Options are validated at startup.
    /// </summary>
    /// <remarks>
    /// This is the opt-in escape hatch for driving raw generated gRPC stubs the typed surfaces do not
    /// cover; prefer <see cref="AddLedgerClient(IServiceCollection, IConfiguration)"/> and
    /// <see cref="AddAdminClient(IServiceCollection, IConfiguration)"/> for everything they cover.
    /// The factory owns a channel of its own, built from the same <see cref="LedgerClientOptions"/> the
    /// clients use, so it needs neither of them registered and registration order does not matter. That
    /// channel is an additional HTTP/2 connection to the same endpoint, and the container disposes it.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// A configuration section containing <see cref="LedgerClientOptions"/> values
    /// (e.g., <c>configuration.GetSection("Canton:Ledger")</c>).
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLedgerRawGrpc(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AddLedgerOptions(services, configuration);
        AddRawGrpcRegistration(services);

        return services;
    }

    /// <summary>
    /// Registers <see cref="IGrpcCallInvokerFactory"/> from <c>Canton.Ledger.Grpc.Client.Raw</c> as a
    /// <see cref="ServiceLifetime.Singleton"/>, binds <see cref="LedgerClientOptions"/> from the provided
    /// configuration section, and auto-registers <see cref="ITokenProvider"/> as a
    /// <see cref="Canton.Ledger.Kernel.Authentication.TokenGeneration.ClientCredentialsProvider"/> from the auth configuration section.
    /// If an <see cref="ITokenProvider"/> is already registered, the existing registration is kept.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// A configuration section containing <see cref="LedgerClientOptions"/> values
    /// (e.g., <c>configuration.GetSection("Canton:Ledger")</c>).
    /// </param>
    /// <param name="authConfiguration">
    /// A configuration section containing <see cref="Canton.Ledger.Kernel.Authentication.TokenGeneration.ClientCredentialsOptions"/> values
    /// (e.g., <c>configuration.GetSection("Canton:Auth")</c>).
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLedgerRawGrpc(
        this IServiceCollection services,
        IConfiguration configuration,
        IConfiguration authConfiguration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(authConfiguration);

        services.AddCantonAuth(authConfiguration);
        AddLedgerOptions(services, configuration);
        AddRawGrpcRegistration(services);

        return services;
    }

    /// <summary>
    /// Registers <see cref="IGrpcCallInvokerFactory"/> from <c>Canton.Ledger.Grpc.Client.Raw</c> as a
    /// <see cref="ServiceLifetime.Singleton"/> and configures <see cref="LedgerClientOptions"/> using the
    /// provided action delegate. Options are validated at startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure <see cref="LedgerClientOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLedgerRawGrpc(this IServiceCollection services, Action<LedgerClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        AddLedgerOptions(services, configure);
        AddRawGrpcRegistration(services);

        return services;
    }

    private static void AddRawGrpcRegistration(IServiceCollection services) =>
        services.TryAddSingleton<IGrpcCallInvokerFactory>(static sp => new GrpcCallInvokerFactory(
            sp.GetRequiredService<IOptions<LedgerClientOptions>>(),
            sp.GetRequiredService<ITokenProvider>(),
            sp.GetService<ILogger<GrpcCallInvokerFactory>>()));

    private static void AddAdminClientRegistration(IServiceCollection services) =>
        services.TryAddSingleton<IAdminClient>(static sp => new AdminClient(
            sp.GetRequiredService<IOptions<LedgerClientOptions>>(),
            sp.GetRequiredService<ITokenProvider>(),
            sp.GetService<ILogger<AdminClient>>()));

    private static void AddLedgerClientRegistration(IServiceCollection services) =>
        services.AddLedgerAdapter(
            static sp => new LedgerClient(
                sp.GetRequiredService<IOptions<LedgerClientOptions>>(),
                sp.GetRequiredService<ITokenProvider>(),
                sp.GetService<ILogger<LedgerClient>>()),
            ServiceLifetime.Singleton);

    private static void AddLedgerOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<LedgerClientOptions>(configuration)
            .ValidateDataAnnotations();

        services.TryAddSingleton(ITokenProvider.None);
    }

    private static void AddLedgerOptions(IServiceCollection services, Action<LedgerClientOptions> configure)
    {
        services.AddValidatedOptions<LedgerClientOptions>(configure)
            .ValidateDataAnnotations();

        services.TryAddSingleton(ITokenProvider.None);
    }
}
