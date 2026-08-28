// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Testing.Localnet;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Data;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class GrpcLedgerWriterParityTests : LedgerWriterParityTests
{
    private const string GrpcUrlEnv = "CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL";
    private const string DefaultGrpcUrl = "http://localhost:11901";

    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this parity test.";

    private static string DarPath() => Path.Combine(
        AppContext.BaseDirectory, "testdata", "richtypes", "richtypes.dar");

    protected override async Task<CapabilityLane<(ILedgerWriter Writer, Party Owner)>> OpenWriterAsync(
        CancellationToken cancellationToken)
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        var fixture = LocalnetFixture.FromEnvironment();
        var grpcAddress = Environment.GetEnvironmentVariable(GrpcUrlEnv) ?? DefaultGrpcUrl;

        await fixture.UploadDarAsync(DarPath(), cancellationToken).ConfigureAwait(false);
        var party = await fixture.AllocatePartyAsync(
            "grpc-writer-parity", cancellationToken: cancellationToken).ConfigureAwait(false);
        await fixture.GrantUserRightsAsync(
            fixture.ValidatorUserId, actAs: [party.PartyId], cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var client = new LedgerClient(
            new LedgerClientOptions { GrpcAddress = grpcAddress, UserId = fixture.ValidatorUserId },
            new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync));

        return new CapabilityLane<(ILedgerWriter, Party)>((client, new Party(party.PartyId)), async () =>
        {
            await client.DisposeAsync().ConfigureAwait(false);
            await fixture.DisposeAsync().ConfigureAwait(false);
        });
    }
}
