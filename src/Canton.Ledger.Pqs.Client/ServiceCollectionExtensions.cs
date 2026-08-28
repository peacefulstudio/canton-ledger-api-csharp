// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Canton.Ledger.Pqs.Client;

/// <summary>
/// Extension methods for registering <see cref="IPqsClient"/> with the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IPqsClient"/> as a singleton and binds <see cref="PqsClientOptions"/>
    /// from the provided configuration section. Options are validated at startup.
    /// If an <see cref="NpgsqlDataSource"/> is registered in the container, the client opens its
    /// connections from that pooled data source; otherwise it opens connections directly from
    /// <see cref="PqsClientOptions.ConnectionString"/>. <see cref="PqsClientOptions.ConnectionString"/>
    /// is validated on start in either case, so it must be set even when a data source is supplied.
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

        services.AddValidatedOptions<PqsClientOptions>(configuration)
            .ValidateDataAnnotations();

        services.TryAddSingleton<IPqsClient>(CreateClient);

        return services;
    }

    /// <summary>
    /// Registers <see cref="IPqsClient"/> as a singleton and configures <see cref="PqsClientOptions"/>
    /// using the provided action delegate. Options are validated at startup.
    /// If an <see cref="NpgsqlDataSource"/> is registered in the container, the client opens its
    /// connections from that pooled data source; otherwise it opens connections directly from
    /// <see cref="PqsClientOptions.ConnectionString"/>. <see cref="PqsClientOptions.ConnectionString"/>
    /// is validated on start in either case, so it must be set even when a data source is supplied.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure <see cref="PqsClientOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPqsClient(this IServiceCollection services, Action<PqsClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddValidatedOptions<PqsClientOptions>(configure)
            .ValidateDataAnnotations();

        services.TryAddSingleton<IPqsClient>(CreateClient);

        return services;
    }

    private static PqsClient CreateClient(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<PqsClientOptions>>().Value;
        var dataSource = provider.GetService<NpgsqlDataSource>();
        var logger = provider.GetService<ILogger<PqsClient>>();

        if (dataSource is null)
        {
            return new PqsClient(options, openConnectionAsync: null, logger);
        }

        return new PqsClient(options, dataSource.OpenConnectionAsync, logger);
    }
}
