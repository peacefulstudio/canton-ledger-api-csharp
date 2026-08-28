// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Testing.Localnet;
using Daml.Ledger.Abstractions;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class GrpcLedgerReaderParityTests : LedgerReaderParityTests
{
    private const string GrpcUrlEnv = "CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL";
    private const string DefaultGrpcUrl = "http://localhost:11901";

    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this parity test.";

    protected override async Task<CapabilityLane<ILedgerReader>> OpenReaderAsync(CancellationToken cancellationToken)
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        var fixture = LocalnetFixture.FromEnvironment();
        try
        {
            var grpcAddress = Environment.GetEnvironmentVariable(GrpcUrlEnv) ?? DefaultGrpcUrl;
            var client = new LedgerClient(
                new LedgerClientOptions { GrpcAddress = grpcAddress, UserId = fixture.ValidatorUserId },
                new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync));

            return new CapabilityLane<ILedgerReader>(client, async () =>
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
