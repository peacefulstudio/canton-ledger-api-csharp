// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Canton.Ledger.Kernel.DependencyInjection;

/// <summary>
/// Shared helpers for registering options that are bound and validated eagerly at startup.
/// </summary>
public static class OptionsServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TOptions"/>, binds it from <paramref name="configuration"/>, and
    /// marks it for eager validation at startup via <see cref="OptionsBuilderExtensions.ValidateOnStart{TOptions}"/>.
    /// Chain <see cref="OptionsBuilderDataAnnotationsExtensions.ValidateDataAnnotations{TOptions}"/> or a custom
    /// <see cref="IValidateOptions{TOptions}"/> onto the returned builder to supply the validation rules.
    /// </summary>
    /// <typeparam name="TOptions">The options type to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration section to bind <typeparamref name="TOptions"/> from.</param>
    /// <returns>The <see cref="OptionsBuilder{TOptions}"/> for further validation configuration.</returns>
    public static OptionsBuilder<TOptions> AddValidatedOptions<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddOptions<TOptions>()
            .Bind(configuration)
            .ValidateOnStart();
    }

    /// <summary>
    /// Registers <typeparamref name="TOptions"/>, configures it with <paramref name="configure"/>, and
    /// marks it for eager validation at startup via <see cref="OptionsBuilderExtensions.ValidateOnStart{TOptions}"/>.
    /// Chain <see cref="OptionsBuilderDataAnnotationsExtensions.ValidateDataAnnotations{TOptions}"/> or a custom
    /// <see cref="IValidateOptions{TOptions}"/> onto the returned builder to supply the validation rules.
    /// </summary>
    /// <typeparam name="TOptions">The options type to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The delegate that configures <typeparamref name="TOptions"/>.</param>
    /// <returns>The <see cref="OptionsBuilder{TOptions}"/> for further validation configuration.</returns>
    public static OptionsBuilder<TOptions> AddValidatedOptions<TOptions>(
        this IServiceCollection services,
        Action<TOptions> configure)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        return services.AddOptions<TOptions>()
            .Configure(configure)
            .ValidateOnStart();
    }
}
