// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Rest.Client.Raw;
using Canton.Ledger.Testing.Localnet;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Integration.Tests;

internal sealed class RestConformanceLane : IAsyncDisposable
{
    internal const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this conformance test.";

    private readonly ServiceProvider _services;
    private readonly ActAsRightsLease _actAsRights;

    private RestConformanceLane(LocalnetFixture fixture, ServiceProvider services)
    {
        Fixture = fixture;
        _services = services;
        _actAsRights = ActAsRightsLease.ForValidator(fixture);
    }

    internal LocalnetFixture Fixture { get; }

    internal RestLedgerClient LedgerClient => _services.GetRequiredService<RestLedgerClient>();

    internal TApi Api<TApi>() where TApi : notnull => _services.GetRequiredService<TApi>();

    internal HttpClient CreateWireLevelClient() =>
        _services.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ServiceCollectionExtensions.HttpClientName);

    /// <summary>
    /// Authorizes the validator user to act as <paramref name="partyId"/> for as long as this lane
    /// is open. The right is revoked when the lane disposes.
    /// </summary>
    internal Task GrantActAsAsync(string partyId, CancellationToken cancellationToken) =>
        _actAsRights.GrantAsync(partyId, cancellationToken);

    internal static async Task<RestConformanceLane> OpenAsync(
        CancellationToken cancellationToken, RecordingRequestHandler? recordingHandler = null)
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        var fixture = LocalnetFixture.FromEnvironment();
        ServiceProvider? services = null;
        try
        {
            var registrations = new ServiceCollection()
                .AddSingleton<ITokenProvider>(new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync))
                .AddRestLedgerRawApis(options => options.HttpAddress = fixture.Endpoints.JsonLedgerApi.ToString())
                .AddRestLedgerClient(options => options.HttpAddress = fixture.Endpoints.JsonLedgerApi.ToString());

            if (recordingHandler is not null)
            {
                registrations
                    .AddHttpClient(ServiceCollectionExtensions.HttpClientName)
                    .AddHttpMessageHandler(() => recordingHandler);
            }

            services = registrations.BuildServiceProvider();

            await LedgerApiVersionSkewGuard.AssertConformableAsync(
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
        try
        {
            await _services.DisposeAsync();
        }
        finally
        {
            try
            {
                await _actAsRights.DisposeAsync();
            }
            finally
            {
                await Fixture.DisposeAsync();
            }
        }
    }
}
