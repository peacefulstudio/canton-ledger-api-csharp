// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Rest.Client;
using Canton.Ledger.Rest.Client.Integration.Tests;
using Canton.Ledger.Rest.Client.Raw;
using Canton.Ledger.Testing.Localnet;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using Richtypes;
using Xunit;
using RuntimeCreateCommand = Daml.Runtime.Commands.CreateCommand;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class RestLedgerCompletionParityTests : LedgerCompletionParityTests
{
    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
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
        ServiceProvider? services = null;
        try
        {
            var darOutcome = await fixture.UploadDarAsync(DarPath(), cancellationToken).ConfigureAwait(false);
            Assert.True(
                darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
                $"Unexpected DAR upload outcome: {darOutcome}");

            var party = await fixture.AllocatePartyAsync(
                "rest-completion-parity", cancellationToken: cancellationToken).ConfigureAwait(false);
            var owner = new Party(party.PartyId);
            var userId = fixture.ValidatorUserId;
            await fixture.GrantUserRightsAsync(
                userId, actAs: [party.PartyId], cancellationToken: cancellationToken).ConfigureAwait(false);

            var jsonAddress = fixture.Endpoints.JsonLedgerApi.ToString();
            services = new ServiceCollection()
                .AddSingleton<ITokenProvider>(new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync))
                .AddRestLedgerRawApis(options => options.HttpAddress = jsonAddress)
                .AddRestLedgerClient(options =>
                {
                    options.HttpAddress = jsonAddress;
                    options.UserId = userId;
                })
                .BuildServiceProvider();

            await LedgerApiVersionSkewGuard.AssertConformableAsync(
                services.GetRequiredService<IVersionServiceApi>(), cancellationToken).ConfigureAwait(false);

            var client = services.GetRequiredService<ICantonLedgerClient>();

            var preSubmitOffset = (await client.GetLedgerEndAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false)).Value;
            var submission = CommandsSubmission
                .Single(RuntimeCreateCommand.For(new Marker(owner)))
                .WithActAs(owner)
                .WithCommandId(new CommandId(Guid.NewGuid().ToString()));
            var returnedCommandId = await client.SubmitAsync(submission, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var probe = new CompletionProbe(client, owner, preSubmitOffset, returnedCommandId);
            return new CapabilityLane<CompletionProbe>(probe, async () =>
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
