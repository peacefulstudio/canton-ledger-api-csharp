// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing.Localnet;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using RichTypes;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

[Trait("Category", "Integration")]
public class D1CoreSurfaceRoundTripTests
{
    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this integration test.";

    private static string DarPath() => Path.Combine(
        AppContext.BaseDirectory, "testdata", "richtypes", "richtypes.dar");

    [Fact]
    public async Task GetLedgerApiVersionAsync_returns_a_nonempty_version()
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        await using var fixture = LocalnetFixture.FromEnvironment();
        await using var services = LocalnetLedgerServices.ForValidator(fixture, fixture.ValidatorUserId);
        var client = services.GetRequiredService<ICantonLedgerClient>();

        var version = await client.GetLedgerApiVersionAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(version), "Ledger API version must not be empty");
    }

    [Fact]
    public async Task GetConnectedSynchronizersAsync_reports_at_least_one_synchronizer_for_an_allocated_party()
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        await using var fixture = LocalnetFixture.FromEnvironment();
        var party = await fixture.AllocatePartyAsync("cdg", cancellationToken: TestContext.Current.CancellationToken);
        var owner = new Party(party.PartyId);
        await using var services = LocalnetLedgerServices.ForValidator(fixture, fixture.ValidatorUserId);
        var client = services.GetRequiredService<ICantonLedgerClient>();

        var synchronizers = await client.GetConnectedSynchronizersAsync(
            owner, cancellationToken: TestContext.Current.CancellationToken);

        var synchronizer = Assert.Single(synchronizers);
        Assert.False(string.IsNullOrWhiteSpace(synchronizer.SynchronizerId), "synchronizer id must not be empty");
        Assert.NotEqual(SynchronizerPermissionLevel.Unrecognized, synchronizer.Permission);
        Assert.NotEqual(SynchronizerPermissionLevel.Unspecified, synchronizer.Permission);
    }

    [Fact]
    public async Task GetUpdateByOffsetAsync_and_GetUpdateByIdAsync_read_back_a_known_transaction()
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        await using var fixture = LocalnetFixture.FromEnvironment();

        var darOutcome = await fixture.UploadDarAsync(DarPath(), TestContext.Current.CancellationToken);
        Assert.True(
            darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
            $"Unexpected DAR upload outcome: {darOutcome}");

        var party = await fixture.AllocatePartyAsync("cdg", cancellationToken: TestContext.Current.CancellationToken);
        var owner = new Party(party.PartyId);
        var userId = fixture.ValidatorUserId;
        await using var actAsRights = ActAsRightsLease.ForValidator(fixture);
        await actAsRights.GrantAsync(party.PartyId, TestContext.Current.CancellationToken);

        await using var services = LocalnetLedgerServices.ForValidator(fixture, userId);
        var client = services.GetRequiredService<ICantonLedgerClient>();

        var submission = RuntimeCommands.CommandsSubmission
            .Single(RuntimeCommands.CreateCommand.For(new Marker(owner)))
            .WithActAs(owner)
            .WithCommandId(new RuntimeCommands.CommandId(Guid.NewGuid().ToString()));

        var submitOutcome = await client.TrySubmitAndWaitForTransactionAsync(
            submission, owner, cancellationToken: TestContext.Current.CancellationToken);
        var submitted = Assert.IsType<ExerciseOutcome<TransactionResult>.One>(submitOutcome).Result;
        var createdContractId = Assert.Single(submitted.CreatedContracts).ContractId;

        var byOffset = await client.GetUpdateByOffsetAsync(
            submitted.CompletionOffset.Value, owner, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(submitted.UpdateId, byOffset.UpdateId);
        Assert.Contains(byOffset.CreatedContracts, c => c.ContractId == createdContractId);

        var byId = await client.GetUpdateByIdAsync(
            submitted.UpdateId, owner, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(submitted.UpdateId, byId.UpdateId);
        Assert.Contains(byId.CreatedContracts, c => c.ContractId == createdContractId);
    }
}
