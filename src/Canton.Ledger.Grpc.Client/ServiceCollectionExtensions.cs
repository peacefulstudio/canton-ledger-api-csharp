// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// Extension methods for registering Canton Ledger API clients with the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ILedgerClient"/> as a singleton and binds <see cref="LedgerClientOptions"/>
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
        services.TryAddSingleton<ILedgerClient, LedgerClient>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="ILedgerClient"/> as a singleton and configures <see cref="LedgerClientOptions"/>
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
        services.TryAddSingleton<ILedgerClient, LedgerClient>();

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
    /// Note: despite the method name, the health check registered by
    /// <see cref="HealthCheckBuilderExtensions.AddLedgerClient"/> depends on <see cref="IAdminClient"/>
    /// because it calls <see cref="IAdminClient.GetParticipantIdAsync"/> to verify connectivity.
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
        services.TryAddSingleton<IAdminClient, AdminClient>();

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
        services.TryAddSingleton<IAdminClient, AdminClient>();

        return services;
    }

    private static void AddLedgerOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LedgerClientOptions>()
            .Bind(configuration)
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }

    private static void AddLedgerOptions(IServiceCollection services, Action<LedgerClientOptions> configure)
    {
        services.AddOptions<LedgerClientOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }
}
