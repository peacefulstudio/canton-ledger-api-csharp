// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Canton.Ledger.Auth.TokenGeneration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Peaceful.Extensions.Logging;

namespace Canton.Ledger.Auth;

/// <summary>
/// Extension methods for registering Canton authentication services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ITokenProvider"/> as a <see cref="ClientCredentialsProvider"/> singleton
    /// and binds <see cref="ClientCredentialsOptions"/> from the provided configuration section.
    /// If an <see cref="ITokenProvider"/> is already registered, the existing registration is kept.
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

        services.AddOptions<ClientCredentialsOptions>()
            .Bind(configuration)
            .ValidateOnStart();

        AddSharedServices(services);

        return services;
    }

    /// <summary>
    /// Registers <see cref="ITokenProvider"/> as a <see cref="ClientCredentialsProvider"/> singleton
    /// and configures <see cref="ClientCredentialsOptions"/> using the provided action delegate.
    /// If an <see cref="ITokenProvider"/> is already registered, the existing registration is kept.
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

        services.AddOptions<ClientCredentialsOptions>()
            .Configure(configure)
            .ValidateOnStart();

        AddSharedServices(services);

        return services;
    }

    /// <summary>
    /// Registers <see cref="ITokenProvider"/> as a <see cref="StaticTokenProvider"/> singleton
    /// that always returns the specified token.
    /// If an <see cref="ITokenProvider"/> is already registered, the existing registration is kept.
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

        services.TryAddSingleton<ITokenProvider>(sp =>
        {
            ConfigureLogging(sp);
            return new StaticTokenProvider(token);
        });

        return services;
    }

    private static void AddSharedServices(IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ClientCredentialsOptions>, ClientCredentialsOptionsValidator>());
        services.AddHttpClient("CantonAuth");
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITokenProvider>(sp =>
        {
            ConfigureLogging(sp);
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var options = sp.GetRequiredService<IOptions<ClientCredentialsOptions>>();
            var timeProvider = sp.GetRequiredService<TimeProvider>();
            return new ClientCredentialsProvider(options, httpClientFactory, timeProvider);
        });
    }

    private static void ConfigureLogging(IServiceProvider sp)
    {
        var loggerFactory = sp.GetService<ILoggerFactory>();
        if (loggerFactory is not null)
            StaticLoggerFactory.Configure(loggerFactory);
    }
}
