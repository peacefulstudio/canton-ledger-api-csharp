// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Refit;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Extension methods for registering the Canton JSON Ledger API interfaces with the
/// dependency injection container: every Refitter-generated per-service interface from
/// <c>Canton.Ledger.Rest</c> plus the hand-authored off-spec trio, all built over one
/// named <see cref="HttpClient"/> carrying the bearer-auth and activity handlers.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The name of the <see cref="HttpClient"/> the interfaces are built over. Hosts can
    /// customize it further via <c>services.AddHttpClient(HttpClientName)</c>.
    /// </summary>
    public const string HttpClientName = "Canton.Ledger.Rest";

    /// <summary>
    /// Registers the JSON Ledger API interfaces and binds <see cref="RestLedgerClientOptions"/>
    /// from the provided configuration section. Options are validated at startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// A configuration section containing <see cref="RestLedgerClientOptions"/> values
    /// (e.g., <c>configuration.GetSection("Canton:Rest")</c>).
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRestLedgerApis(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<RestLedgerClientOptions>(configuration)
            .ValidateDataAnnotations();

        return AddRestApisCore(services);
    }

    /// <summary>
    /// Registers the JSON Ledger API interfaces, binds <see cref="RestLedgerClientOptions"/>
    /// from the provided configuration section, and auto-registers <see cref="ITokenProvider"/> as a
    /// <see cref="Canton.Ledger.Kernel.Authentication.TokenGeneration.ClientCredentialsProvider"/>
    /// from the auth configuration section.
    /// If an <see cref="ITokenProvider"/> is already registered, the existing registration is kept.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// A configuration section containing <see cref="RestLedgerClientOptions"/> values
    /// (e.g., <c>configuration.GetSection("Canton:Rest")</c>).
    /// </param>
    /// <param name="authConfiguration">
    /// A configuration section containing <see cref="Canton.Ledger.Kernel.Authentication.TokenGeneration.ClientCredentialsOptions"/> values
    /// (e.g., <c>configuration.GetSection("Canton:Auth")</c>).
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRestLedgerApis(
        this IServiceCollection services,
        IConfiguration configuration,
        IConfiguration authConfiguration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(authConfiguration);

        services.AddCantonAuth(authConfiguration);

        return services.AddRestLedgerApis(configuration);
    }

    /// <summary>
    /// Registers the JSON Ledger API interfaces and configures <see cref="RestLedgerClientOptions"/>
    /// using the provided action delegate. Options are validated at startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure <see cref="RestLedgerClientOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRestLedgerApis(this IServiceCollection services, Action<RestLedgerClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddValidatedOptions<RestLedgerClientOptions>(configure)
            .ValidateDataAnnotations();

        return AddRestApisCore(services);
    }

    private static IServiceCollection AddRestApisCore(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(RestApisRegisteredMarker)))
            return services;
        services.AddSingleton<RestApisRegisteredMarker>();

        services.TryAddSingleton(ITokenProvider.None);
        services.TryAddTransient(static sp => new BearerTokenHandler(
            sp.GetRequiredService<ITokenProvider>(),
            sp.GetService<ILogger<BearerTokenHandler>>()));
        services.TryAddTransient<RestActivityHandler>();

        services.AddHttpClient(HttpClientName)
            .ConfigureHttpClient(static (sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<RestLedgerClientOptions>>().Value;
                client.BaseAddress = new Uri(options.HttpAddress, UriKind.Absolute);
            })
            .AddHttpMessageHandler<BearerTokenHandler>()
            .AddHttpMessageHandler<RestActivityHandler>();

        AddApi<ICommandServiceApi>(services);
        AddApi<ICommandSubmissionServiceApi>(services);
        AddApi<ICommandCompletionServiceApi>(services);
        AddApi<IContractServiceApi>(services);
        AddApi<IEventQueryServiceApi>(services);
        AddApi<IIdentityProviderConfigServiceApi>(services);
        AddApi<IInteractiveSubmissionServiceApi>(services);
        AddApi<IPackageManagementServiceApi>(services);
        AddApi<IPackageServiceApi>(services);
        AddApi<IPartyManagementServiceApi>(services);
        AddApi<IStateServiceApi>(services);
        AddApi<IUpdateServiceApi>(services);
        AddApi<IUserManagementServiceApi>(services);
        AddApi<IVersionServiceApi>(services);

        AddApi<IAuthenticatedUserApi>(services);
        AddApi<IHealthApi>(services);

        return services;
    }

    private static void AddApi<TApi>(IServiceCollection services) where TApi : class =>
        services.TryAddTransient(static sp => RestService.For<TApi>(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            RestRefitSettings.Create()));

    private sealed class RestApisRegisteredMarker;
}
