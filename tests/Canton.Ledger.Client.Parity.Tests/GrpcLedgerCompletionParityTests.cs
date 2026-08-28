// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Testing.Localnet;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Peaceful.Canton.Localnet.Testing;
using Richtypes;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class GrpcLedgerCompletionParityTests : LedgerCompletionParityTests
{
    private const string GrpcUrlEnv = "CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL";
    private const string DefaultGrpcUrl = "http://localhost:11901";

    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this parity test.";

    private static string DarPath() => Path.Combine(
        AppContext.BaseDirectory, "testdata", "richtypes", "richtypes.dar");

    protected override async Task<CapabilityLane<CompletionProbe>> OpenCompletionAsync(CancellationToken cancellationToken)
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        var fixture = LocalnetFixture.FromEnvironment();
        try
        {
            var darOutcome = await fixture.UploadDarAsync(DarPath(), cancellationToken);
            Assert.True(
                darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
                $"Unexpected DAR upload outcome: {darOutcome}");

            var party = await fixture.AllocatePartyAsync("cdg", cancellationToken: cancellationToken);
            var owner = new Party(party.PartyId);
            var userId = fixture.ValidatorUserId;
            await fixture.GrantUserRightsAsync(
                userId, actAs: new[] { party.PartyId }, cancellationToken: cancellationToken);

            var grpcAddress = Environment.GetEnvironmentVariable(GrpcUrlEnv) ?? DefaultGrpcUrl;
            var client = new LedgerClient(
                new LedgerClientOptions { GrpcAddress = grpcAddress, UserId = userId },
                new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync));

            var preSubmitOffset = (await client.GetLedgerEndAsync(cancellationToken: cancellationToken)).Value;
            var submission = CommandsSubmission
                .Single(CreateCommand.For(new Marker(owner)))
                .WithActAs(owner)
                .WithCommandId(new CommandId(Guid.NewGuid().ToString()));
            var returnedCommandId = await client.SubmitAsync(submission, cancellationToken);

            var probe = new CompletionProbe(client, owner, preSubmitOffset, returnedCommandId);
            return new CapabilityLane<CompletionProbe>(probe, async () =>
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
