// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Testing.Localnet;
using Daml.Runtime.Data;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class GrpcLedgerTransactionTreeParityTests : LedgerTransactionTreeParityTests
{
    private const string GrpcUrlEnv = "CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL";
    private const string DefaultGrpcUrl = "http://localhost:11901";

    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this parity test.";

    private static string DarPath() => Path.Combine(
        AppContext.BaseDirectory, "testdata", "richtypes", "richtypes.dar");

    protected override async Task<CapabilityLane<(ICantonLedgerClient Client, Party Owner)>> OpenTransactionTreeAsync(
        CancellationToken cancellationToken)
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        var fixture = LocalnetFixture.FromEnvironment();
        try
        {
            await fixture.UploadDarAsync(DarPath(), cancellationToken).ConfigureAwait(false);
            var party = await fixture.AllocatePartyAsync(
                "grpc-tree-parity", cancellationToken: cancellationToken).ConfigureAwait(false);
            await fixture.GrantUserRightsAsync(
                fixture.ValidatorUserId, actAs: [party.PartyId], cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var grpcAddress = Environment.GetEnvironmentVariable(GrpcUrlEnv) ?? DefaultGrpcUrl;
            var client = new LedgerClient(
                new LedgerClientOptions { GrpcAddress = grpcAddress, UserId = fixture.ValidatorUserId },
                new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync));

            return new CapabilityLane<(ICantonLedgerClient, Party)>((client, new Party(party.PartyId)), async () =>
            {
                try
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    await fixture.DisposeAsync().ConfigureAwait(false);
                }
            });
        }
        catch
        {
            await fixture.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
