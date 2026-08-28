// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.DependencyInjection;
using Canton.Ledger.Rest.Client.Raw;
using Daml.Ledger.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Refit;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Extension methods for registering the Canton JSON Ledger API client with the dependency
/// injection container. <see cref="AddRestLedgerClient(IServiceCollection, IConfiguration)"/>
/// registers the supported <see cref="RestLedgerClient"/> adapter behind the full Canton participant
/// surface <see cref="Canton.Ledger.Abstractions.ICantonLedgerClient"/> (and the base
/// <see cref="ILedgerClient"/>, backed by the same instance); <see cref="AddRestLedgerRawApis(IServiceCollection, IConfiguration)"/>
/// opts into the low-level Refitter-generated per-service interfaces from
/// <c>Canton.Ledger.Rest.Client.Raw</c>. Both build over one named <see cref="HttpClient"/> carrying
/// the bearer-auth and activity handlers.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The name of the <see cref="HttpClient"/> the client is built over. Hosts can
    /// customize it further via <c>services.AddHttpClient(HttpClientName)</c>.
    /// </summary>
    public const string HttpClientName = "Canton.Ledger.Rest";

    /// <summary>
    /// Registers the supported <see cref="RestLedgerClient"/> adapter as <see cref="ILedgerClient"/> (<see cref="ILedgerReader"/>, <see cref="ILedgerWriter"/>, <see cref="ILedgerStreamer"/>)
    /// and binds <see cref="RestLedgerClientOptions"/> from the provided configuration section.
    /// Options are validated at startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// A configuration section containing <see cref="RestLedgerClientOptions"/> values
    /// (e.g., <c>configuration.GetSection("Canton:Rest")</c>).
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRestLedgerClient(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AddRestLedgerOptions(services, configuration);

        return AddLedgerReaderAdapter(services);
    }

    /// <summary>
    /// Registers the supported <see cref="RestLedgerClient"/> adapter as <see cref="ILedgerClient"/> (<see cref="ILedgerReader"/>, <see cref="ILedgerWriter"/>, <see cref="ILedgerStreamer"/>),
    /// binds <see cref="RestLedgerClientOptions"/> from the provided configuration section, and
    /// auto-registers <see cref="ITokenProvider"/> as a
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
    public static IServiceCollection AddRestLedgerClient(
        this IServiceCollection services,
        IConfiguration configuration,
        IConfiguration authConfiguration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(authConfiguration);

        services.AddCantonAuth(authConfiguration);

        return services.AddRestLedgerClient(configuration);
    }

    /// <summary>
    /// Registers the supported <see cref="RestLedgerClient"/> adapter as <see cref="ILedgerClient"/> (<see cref="ILedgerReader"/>, <see cref="ILedgerWriter"/>, <see cref="ILedgerStreamer"/>)
    /// and configures <see cref="RestLedgerClientOptions"/> using the provided action delegate.
    /// Options are validated at startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure <see cref="RestLedgerClientOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRestLedgerClient(this IServiceCollection services, Action<RestLedgerClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        AddRestLedgerOptions(services, configure);

        return AddLedgerReaderAdapter(services);
    }

    /// <summary>
    /// Registers the low-level Refitter-generated JSON Ledger API interfaces from
    /// <c>Canton.Ledger.Rest.Client.Raw</c> (and the hand-authored off-spec interfaces) and binds
    /// <see cref="RestLedgerClientOptions"/> from the provided configuration section. Options are
    /// validated at startup. Prefer <see cref="AddRestLedgerClient(IServiceCollection, IConfiguration)"/>
    /// for the supported adapter.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// A configuration section containing <see cref="RestLedgerClientOptions"/> values
    /// (e.g., <c>configuration.GetSection("Canton:Rest")</c>).
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    [Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
    public static IServiceCollection AddRestLedgerRawApis(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AddRestLedgerOptions(services, configuration);

        return AddRawApis(services);
    }

    /// <summary>
    /// Registers the low-level Refitter-generated JSON Ledger API interfaces from
    /// <c>Canton.Ledger.Rest.Client.Raw</c> (and the hand-authored off-spec interfaces), binds
    /// <see cref="RestLedgerClientOptions"/> from the provided configuration section, and
    /// auto-registers <see cref="ITokenProvider"/> as a
    /// <see cref="Canton.Ledger.Kernel.Authentication.TokenGeneration.ClientCredentialsProvider"/>
    /// from the auth configuration section.
    /// If an <see cref="ITokenProvider"/> is already registered, the existing registration is kept.
    /// Prefer <see cref="AddRestLedgerClient(IServiceCollection, IConfiguration, IConfiguration)"/>
    /// for the supported adapter.
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
    [Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
    public static IServiceCollection AddRestLedgerRawApis(
        this IServiceCollection services,
        IConfiguration configuration,
        IConfiguration authConfiguration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(authConfiguration);

        services.AddCantonAuth(authConfiguration);

        return services.AddRestLedgerRawApis(configuration);
    }

    /// <summary>
    /// Registers the low-level Refitter-generated JSON Ledger API interfaces from
    /// <c>Canton.Ledger.Rest.Client.Raw</c> (and the hand-authored off-spec interfaces) and configures
    /// <see cref="RestLedgerClientOptions"/> using the provided action delegate. Options are
    /// validated at startup. Prefer <see cref="AddRestLedgerClient(IServiceCollection, Action{RestLedgerClientOptions})"/>
    /// for the supported adapter.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure <see cref="RestLedgerClientOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    [Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
    public static IServiceCollection AddRestLedgerRawApis(this IServiceCollection services, Action<RestLedgerClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        AddRestLedgerOptions(services, configure);

        return AddRawApis(services);
    }

    private static IServiceCollection AddLedgerReaderAdapter(IServiceCollection services)
    {
        AddRestLedgerCore(services);

        services.TryAddTransient(static sp => new RestLedgerClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IOptions<RestLedgerClientOptions>>(),
            sp.GetService<ILogger<RestLedgerClient>>()));
        services.TryAddTransient<ILedgerReader>(static sp => sp.GetRequiredService<RestLedgerClient>());
        services.TryAddTransient<ILedgerWriter>(static sp => sp.GetRequiredService<RestLedgerClient>());
        services.TryAddTransient<ILedgerStreamer>(static sp => sp.GetRequiredService<RestLedgerClient>());
        services.TryAddTransient<ILedgerClient>(static sp => sp.GetRequiredService<RestLedgerClient>());
        services.TryAddTransient<Canton.Ledger.Abstractions.ICantonLedgerClient>(static sp => sp.GetRequiredService<RestLedgerClient>());

        return services;
    }

    private static IServiceCollection AddRawApis(IServiceCollection services)
    {
        AddRestLedgerCore(services);

#pragma warning disable CANTONREST001
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
        AddApi<IDarApi>(services);
        AddApi<IHealthApi>(services);
        AddApi<IInteractiveSubmissionApi>(services);
        AddApi<IPackageApi>(services);
#pragma warning restore CANTONREST001

        return services;
    }

    private static IServiceCollection AddRestLedgerCore(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(RestApisRegisteredMarker)))
            return services;
        services.AddSingleton<RestApisRegisteredMarker>();

        services.TryAddSingleton(ITokenProvider.None);
        services.TryAddTransient(static sp => new BearerTokenHandler(
            sp.GetRequiredService<ITokenProvider>(),
            sp.GetService<ILogger<BearerTokenHandler>>()));
        services.TryAddTransient<RestActivityHandler>();
        services.TryAddTransient(static sp => new RestRetryHandler(
            sp.GetRequiredService<IOptions<RestLedgerClientOptions>>()));

        services.AddHttpClient(HttpClientName)
            .ConfigureHttpClient(static (sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<RestLedgerClientOptions>>().Value;
                client.BaseAddress = new Uri(options.HttpAddress, UriKind.Absolute);
            })
            .AddHttpMessageHandler<RestRetryHandler>()
            .AddHttpMessageHandler<BearerTokenHandler>()
            .AddHttpMessageHandler<RestActivityHandler>();

        return services;
    }

    private static void AddApi<TApi>(IServiceCollection services) where TApi : class =>
        services.TryAddTransient(static sp => RestService.For<TApi>(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            RestRefitSettings.Create()));

    private static void AddRestLedgerOptions(IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(d => d.ServiceType == typeof(RestOptionsRegisteredMarker)))
            return;
        services.AddSingleton<RestOptionsRegisteredMarker>();
        services.AddValidatedOptions<RestLedgerClientOptions>(configuration)
            .ValidateDataAnnotations();
    }

    private static void AddRestLedgerOptions(IServiceCollection services, Action<RestLedgerClientOptions> configure)
    {
        if (services.Any(d => d.ServiceType == typeof(RestOptionsRegisteredMarker)))
            return;
        services.AddSingleton<RestOptionsRegisteredMarker>();
        services.AddValidatedOptions<RestLedgerClientOptions>(configure)
            .ValidateDataAnnotations();
    }

    private sealed class RestApisRegisteredMarker;
    private sealed class RestOptionsRegisteredMarker;
}
