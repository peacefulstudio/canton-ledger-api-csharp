// Copyright © 2026 Peaceful Studio OÜ. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Canton.Ledger.Pqs.Client;

/// <summary>
/// Extension methods for registering <see cref="IPqsClient"/> with the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IPqsClient"/> as a singleton and binds <see cref="PqsClientOptions"/>
    /// from the provided configuration section. Options are validated at startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// A configuration section containing <see cref="PqsClientOptions"/> values
    /// (e.g., <c>configuration.GetSection("Canton:Pqs")</c>).
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPqsClient(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<PqsClientOptions>()
            .Bind(configuration)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<IPqsClient, PqsClient>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IPqsClient"/> as a singleton and configures <see cref="PqsClientOptions"/>
    /// using the provided action delegate. Options are validated at startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure <see cref="PqsClientOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPqsClient(this IServiceCollection services, Action<PqsClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<PqsClientOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<IPqsClient, PqsClient>();

        return services;
    }
}
