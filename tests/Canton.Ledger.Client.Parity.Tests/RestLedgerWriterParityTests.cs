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
public sealed class RestLedgerWriterParityTests : LedgerWriterParityTests
{
    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this parity test.";

    private const string ExerciseResultQuarantineMessage =
        "Quarantined on the exercise response, not on the submission: the participant accepts the "
        + "exercise and commits it. The client now requests the ledger-effects transaction shape on "
        + "submit-and-wait-for-transaction, so the transaction does surface an ExercisedEvent, whose "
        + "choiceArgument and exerciseResult then both arrive as {}, an untyped wire Value with no "
        + "sum case set that the decoder cannot resolve without knowing the Daml type. That cause "
        + "was measured against a live participant. The create and fire-and-forget bodies of this "
        + "suite decode no choice result and run on this lane. The quarantine lifts when the decode "
        + "fix lands; the interim participant behavior is pinned by RestExerciseQuarantinePinTests "
        + "in Canton.Ledger.Rest.Client.Tests.";

    /// <inheritdoc />
    protected override string? ExerciseResultDecodeQuarantine => ExerciseResultQuarantineMessage;

    private static string DarPath() => Path.Combine(
        AppContext.BaseDirectory, "testdata", "richtypes", "richtypes.dar");

    protected override async Task<CapabilityLane<(ILedgerWriter Writer, ICantonLedgerClient Client, Party Owner)>> OpenWriterAsync(
        CancellationToken cancellationToken)
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
                "rest-writer-parity", cancellationToken: cancellationToken).ConfigureAwait(false);
            await fixture.GrantUserRightsAsync(
                fixture.ValidatorUserId, actAs: [party.PartyId], cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var writer = services.GetRequiredService<ILedgerWriter>();
            var client = services.GetRequiredService<ICantonLedgerClient>();
            return new CapabilityLane<(ILedgerWriter, ICantonLedgerClient, Party)>(
                (writer, client, new Party(party.PartyId)),
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
