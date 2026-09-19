// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net.Security;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication.TokenGeneration;
using Canton.Ledger.Kernel.DependencyInjection;
using Canton.Ledger.Kernel.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Canton.Ledger.Kernel.Authentication;

/// <summary>
/// Extension methods for registering Canton authentication services.
/// </summary>
public static class ServiceCollectionExtensions
{
    private const string HttpClientName = "CantonAuth";

    /// <summary>
    /// Registers <see cref="ITokenProvider"/> as a <see cref="ClientCredentialsProvider"/> singleton
    /// and binds <see cref="ClientCredentialsOptions"/> from the provided configuration section.
    /// Existing unkeyed providers are kept except for the exact unkeyed
    /// <see cref="ITokenProvider.None"/> singleton instance, which is replaced by client credentials.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// A configuration section containing <see cref="ClientCredentialsOptions"/> values
    /// (e.g., <c>configuration.GetSection("Canton:Auth")</c>).
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCantonAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<ClientCredentialsOptions>(configuration);

        AddSharedServices(services);

        return services;
    }

    /// <summary>
    /// Registers <see cref="ITokenProvider"/> as a <see cref="ClientCredentialsProvider"/> singleton
    /// and configures <see cref="ClientCredentialsOptions"/> using the provided action delegate.
    /// Existing unkeyed providers are kept except for the exact unkeyed
    /// <see cref="ITokenProvider.None"/> singleton instance, which is replaced by client credentials.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure <see cref="ClientCredentialsOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCantonAuth(
        this IServiceCollection services,
        Action<ClientCredentialsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddValidatedOptions<ClientCredentialsOptions>(configure);

        AddSharedServices(services);

        return services;
    }

    /// <summary>
    /// Registers <see cref="ITokenProvider"/> as a <see cref="StaticTokenProvider"/> singleton
    /// that always returns the specified token unless a non-fallback unkeyed provider already exists.
    /// Replaces only the exact unkeyed <see cref="ITokenProvider.None"/> fallback.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="token">The static bearer token.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCantonStaticAuth(
        this IServiceCollection services,
        string token)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        RemoveNoneTokenProviderFallbacks(services);
        services.TryAddSingleton<ITokenProvider>(new StaticTokenProvider(token));

        return services;
    }

    private static void AddSharedServices(IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ClientCredentialsOptions>, ClientCredentialsOptionsValidator>());
        services.AddHttpClient(HttpClientName)
            .ConfigureHttpClient((serviceProvider, httpClient) =>
                httpClient.Timeout = serviceProvider
                    .GetRequiredService<IOptions<ClientCredentialsOptions>>()
                    .Value.TokenAcquisitionTimeout);
        services.AddOptions<HttpClientFactoryOptions>(HttpClientName)
            .PostConfigure<IOptions<ClientCredentialsOptions>>(static (factoryOptions, authOptions) =>
            {
                var tls = authOptions.Value.Tls;
                if (!tls.IsConfigured)
                    return;

                var sslOptions = SslClientAuthenticationOptionsFactory.Create(tls);

                factoryOptions.HttpMessageHandlerBuilderActions.Insert(0, builder =>
                {
                    var handler = builder.PrimaryHandler as SocketsHttpHandler
                        ?? new SocketsHttpHandler { PooledConnectionLifetime = factoryOptions.HandlerLifetime };
                    handler.SslOptions = sslOptions;
                    builder.PrimaryHandler = handler;
                });
            });
        services.TryAddSingleton(TimeProvider.System);

        RemoveNoneTokenProviderFallbacks(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ITokenProvider)
            && !descriptor.IsKeyedService))
            return;

        services.AddSingleton<ITokenProvider, ClientCredentialsProvider>();
        services.AddSingleton(new ClientCredentialsRegistration());
    }

    private static void RemoveNoneTokenProviderFallbacks(IServiceCollection services)
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            var descriptor = services[index];
            if (descriptor.ServiceType == typeof(ITokenProvider)
                && !descriptor.IsKeyedService
                && ReferenceEquals(descriptor.ImplementationInstance, ITokenProvider.None))
                services.RemoveAt(index);
        }
    }
}

internal sealed class ClientCredentialsRegistration;
