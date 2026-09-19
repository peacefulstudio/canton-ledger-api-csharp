// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Testing.Localnet;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using RichTypes;
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
        var actAsRights = ActAsRightsLease.ForValidator(fixture);
        ServiceProvider? services = null;
        try
        {
            var darOutcome = await fixture.UploadDarAsync(DarPath(), cancellationToken);
            Assert.True(
                darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
                $"Unexpected DAR upload outcome: {darOutcome}");

            var party = await fixture.AllocatePartyAsync("cdg", cancellationToken: cancellationToken);
            var owner = new Party(party.PartyId);
            var userId = fixture.ValidatorUserId;
            await actAsRights.GrantAsync(party.PartyId, cancellationToken);

            var grpcAddress = Environment.GetEnvironmentVariable(GrpcUrlEnv) ?? DefaultGrpcUrl;
            services = new ServiceCollection()
                .AddSingleton<ITokenProvider>(new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync))
                .AddLedgerClient(options =>
                {
                    options.GrpcAddress = grpcAddress;
                    options.UserId = userId;
                })
                .BuildServiceProvider();

            var client = services.GetRequiredService<ICantonLedgerClient>();

            var preSubmitOffset = (await client.GetLedgerEndAsync(cancellationToken: cancellationToken)).Value;
            var submission = CommandsSubmission
                .Single(CreateCommand.For(new Marker(owner)))
                .WithActAs(owner)
                .WithCommandId(new CommandId(Guid.NewGuid().ToString()));
            var returnedCommandId = await client.SubmitAsync(submission, cancellationToken: cancellationToken);

            var probe = new CompletionProbe(client, owner, preSubmitOffset, returnedCommandId);
            return new CapabilityLane<CompletionProbe>(probe, async () =>
            {
                try
                {
                    await services.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    await LaneTeardown.ReleaseAsync(actAsRights, fixture).ConfigureAwait(false);
                }
            });
        }
        catch (Exception openFailure)
        {
            await LaneTeardown.ReleaseAsync(openFailure, services, actAsRights, fixture)
                .ConfigureAwait(false);
            throw;
        }
    }
}
