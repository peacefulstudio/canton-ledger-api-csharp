// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing.Localnet;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using RichTypes;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

[Trait("Category", "Integration")]
public class SubscribeArchivedRoundTripTests
{
    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this integration test.";

    private static string DarPath() => Path.Combine(
        AppContext.BaseDirectory, "testdata", "richtypes", "richtypes.dar");

    [Fact]
    public async Task SubscribeAsync_delivers_an_Archived_event_when_a_contract_is_archived()
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        await using var fixture = LocalnetFixture.FromEnvironment();
        var bootstrap = await BootstrapAsync(fixture);
        await using var services = bootstrap.Services;
        await using var actAsRights = bootstrap.ActAsRights;
        var owner = bootstrap.Owner;
        var client = services.GetRequiredService<ICantonLedgerClient>();

        var startOffset = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var markerCid = await CreateMarkerAsync(client, owner);
        await ArchiveMarkerAsync(client, owner, markerCid);

        var endOffset = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (created, archived) = await ReadCreatedAndArchivedAsync(client, owner, markerCid.Value, startOffset, endOffset);

        Assert.True(created, $"expected a Created event for {markerCid.Value} on the AcsDelta subscribe stream");
        Assert.True(archived, $"expected an Archived event for {markerCid.Value} on the AcsDelta subscribe stream");
    }

    [Fact]
    public async Task Snapshot_then_resumed_SubscribeAsync_reconstructs_a_consistent_active_set_across_an_archive()
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        await using var fixture = LocalnetFixture.FromEnvironment();
        var bootstrap = await BootstrapAsync(fixture);
        await using var services = bootstrap.Services;
        await using var actAsRights = bootstrap.ActAsRights;
        var owner = bootstrap.Owner;
        var client = services.GetRequiredService<ICantonLedgerClient>();

        var retainedCid = await CreateMarkerAsync(client, owner);
        var archivedCid = await CreateMarkerAsync(client, owner);

        var (snapshotActive, snapshotOffset) = await SnapshotActiveAsync(client, owner);

        Assert.Contains(retainedCid.Value, snapshotActive);
        Assert.Contains(archivedCid.Value, snapshotActive);

        await ArchiveMarkerAsync(client, owner, archivedCid);
        var endOffset = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var reconstructed = new HashSet<string>(snapshotActive);
        var sawArchivedForTarget = false;
        var sawDuplicateCreate = false;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(60));
        await foreach (var streamEvent in client.SubscribeAsync<Marker>(owner, snapshotOffset, endOffset, cts.Token))
        {
            switch (streamEvent)
            {
                case ContractStreamEvent<Marker>.Created created:
                    sawDuplicateCreate |= snapshotActive.Contains(created.ContractId.Value);
                    reconstructed.Add(created.ContractId.Value);
                    break;
                case ContractStreamEvent<Marker>.Archived archived:
                    sawArchivedForTarget |= archived.ContractId.Value == archivedCid.Value;
                    reconstructed.Remove(archived.ContractId.Value);
                    break;
            }
        }

        Assert.True(sawArchivedForTarget, $"resumed stream did not deliver an Archived event for {archivedCid.Value}");
        Assert.False(sawDuplicateCreate, "resumed stream re-delivered a Created already present in the snapshot");
        Assert.Contains(retainedCid.Value, reconstructed);
        Assert.DoesNotContain(archivedCid.Value, reconstructed);
    }

    private static async Task<(ServiceProvider Services, ActAsRightsLease ActAsRights, Party Owner)>
        BootstrapAsync(LocalnetFixture fixture)
    {
        var darOutcome = await fixture.UploadDarAsync(DarPath(), TestContext.Current.CancellationToken);
        Assert.True(
            darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
            $"Unexpected DAR upload outcome: {darOutcome}");

        var party = await fixture.AllocatePartyAsync("cdg", cancellationToken: TestContext.Current.CancellationToken);
        var owner = new Party(party.PartyId);
        var userId = fixture.ValidatorUserId;
        var actAsRights = ActAsRightsLease.ForValidator(fixture);
        try
        {
            await actAsRights.GrantAsync(party.PartyId, TestContext.Current.CancellationToken);
            return (LocalnetLedgerServices.ForValidator(fixture, userId), actAsRights, owner);
        }
        catch
        {
            await actAsRights.DisposeAsync();
            throw;
        }
    }

    private static async Task<ContractId<Marker>> CreateMarkerAsync(ICantonLedgerClient client, Party owner)
    {
        var outcome = await client.CreateAsync(new Marker(owner), TestContext.Current.CancellationToken);
        return Assert.IsType<ExerciseOutcome<ContractId<Marker>>.One>(outcome).Result;
    }

    private static async Task ArchiveMarkerAsync(ICantonLedgerClient client, Party owner, ContractId<Marker> markerCid)
    {
        var archiveCommand = new ExerciseCommand(
            Marker.TemplateId,
            markerCid,
            new ChoiceName("Archive"),
            new DamlRecord(null, []));

        var outcome = await client.TryExerciseAsync<DamlUnit>(
            archiveCommand, owner, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(
            outcome is ExerciseOutcome<DamlUnit>.DamlError or ExerciseOutcome<DamlUnit>.InfraError,
            $"archiving {markerCid.Value} failed: {outcome}");
    }

    private static async Task<(bool Created, bool Archived)> ReadCreatedAndArchivedAsync(
        ICantonLedgerClient client, Party owner, string contractIdValue, LedgerOffset fromOffset, LedgerOffset toOffset)
    {
        var created = false;
        var archived = false;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(60));
        await foreach (var streamEvent in client.SubscribeAsync<Marker>(owner, fromOffset, toOffset, cts.Token))
        {
            switch (streamEvent)
            {
                case ContractStreamEvent<Marker>.Created c when c.ContractId.Value == contractIdValue:
                    created = true;
                    break;
                case ContractStreamEvent<Marker>.Archived a when a.ContractId.Value == contractIdValue:
                    archived = true;
                    break;
            }
        }

        return (created, archived);
    }

    private static async Task<(HashSet<string> Active, LedgerOffset SnapshotOffset)> SnapshotActiveAsync(
        ICantonLedgerClient client, Party owner)
    {
        var active = new HashSet<string>();
        LedgerOffset snapshotOffset = default;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(60));
        await foreach (var entry in client.SubscribeActiveAsync<Marker>(owner, cancellationToken: cts.Token))
        {
            switch (entry)
            {
                case AcsSnapshotEntry<Marker>.Created created:
                    active.Add(created.ContractId.Value);
                    break;
                case AcsSnapshotEntry<Marker>.Checkpoint checkpoint:
                    snapshotOffset = checkpoint.Resume.Offset;
                    break;
            }
        }

        return (active, snapshotOffset);
    }
}
