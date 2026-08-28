// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Rest.Client;
using Canton.Ledger.Rest.Client.Integration.Tests;
using Canton.Ledger.Rest.Client.Raw;
using Canton.Ledger.Testing.Localnet;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Data;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class RestLedgerStreamerParityTests : LedgerStreamerParityTests
{
    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this parity test.";

    private static string DarPath() => Path.Combine(
        AppContext.BaseDirectory, "testdata", "richtypes", "richtypes.dar");

    protected override async Task<CapabilityLane<(ILedgerReader Reader, ILedgerWriter Writer, ILedgerStreamer Streamer, Party Owner)>>
        OpenStreamerAsync(CancellationToken cancellationToken)
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

            await fixture.UploadDarAsync(DarPath(), cancellationToken).ConfigureAwait(false);
            var party = await fixture.AllocatePartyAsync(
                "rest-streamer-parity", cancellationToken: cancellationToken).ConfigureAwait(false);
            await fixture.GrantUserRightsAsync(
                fixture.ValidatorUserId, actAs: [party.PartyId], cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var client = services.GetRequiredService<RestLedgerClient>();
            return new CapabilityLane<(ILedgerReader, ILedgerWriter, ILedgerStreamer, Party)>(
                (client, client, client, new Party(party.PartyId)),
                async () =>
                {
                    await services.DisposeAsync().ConfigureAwait(false);
                    await fixture.DisposeAsync().ConfigureAwait(false);
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
