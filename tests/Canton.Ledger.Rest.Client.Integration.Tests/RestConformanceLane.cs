// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace Canton.Ledger.Rest.Client.Integration.Tests;

internal sealed class RestConformanceLane : IAsyncDisposable
{
    internal const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this conformance test.";

    private readonly ServiceProvider _services;

    private RestConformanceLane(LocalnetFixture fixture, ServiceProvider services)
    {
        Fixture = fixture;
        _services = services;
    }

    internal LocalnetFixture Fixture { get; }

    internal TApi Api<TApi>() where TApi : notnull => _services.GetRequiredService<TApi>();

    internal HttpClient CreateWireLevelClient() =>
        _services.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ServiceCollectionExtensions.HttpClientName);

    internal static async Task<RestConformanceLane> OpenAsync(CancellationToken cancellationToken)
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        var fixture = LocalnetFixture.FromEnvironment();
        ServiceProvider? services = null;
        try
        {
            services = new ServiceCollection()
                .AddSingleton<ITokenProvider>(new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync))
                .AddRestLedgerApis(options => options.HttpAddress = fixture.Endpoints.JsonLedgerApi.ToString())
                .BuildServiceProvider();

            await LedgerApiVersionSkewGuard.AssertConformableOrSkipAsync(
                services.GetRequiredService<IVersionServiceApi>(), cancellationToken);

            return new RestConformanceLane(fixture, services);
        }
        catch
        {
            if (services is not null) await services.DisposeAsync();
            await fixture.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await Fixture.DisposeAsync();
    }
}
