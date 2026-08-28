// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Rest.Client;
using Canton.Ledger.Rest.Client.Integration.Tests;
using Canton.Ledger.Rest.Client.Raw;
using Canton.Ledger.Testing.Localnet;
using Daml.Ledger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class RestLedgerReaderParityTests : LedgerReaderParityTests
{
    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this parity test.";

    protected override async Task<CapabilityLane<ILedgerReader>> OpenReaderAsync(CancellationToken cancellationToken)
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
                .AddRestLedgerRawApis(options => options.HttpAddress = fixture.Endpoints.JsonLedgerApi.ToString())
                .AddRestLedgerClient(options => options.HttpAddress = fixture.Endpoints.JsonLedgerApi.ToString())
                .BuildServiceProvider();

            await LedgerApiVersionSkewGuard.AssertConformableAsync(
                services.GetRequiredService<IVersionServiceApi>(), cancellationToken).ConfigureAwait(false);

            var reader = services.GetRequiredService<RestLedgerClient>();
            return new CapabilityLane<ILedgerReader>(reader, async () =>
            {
                try
                {
                    await services.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    await fixture.DisposeAsync().ConfigureAwait(false);
                }
            });
        }
        catch
        {
            if (services is not null)
            {
                await services.DisposeAsync().ConfigureAwait(false);
            }

            await fixture.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
