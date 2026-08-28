// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Testing.Localnet;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Peaceful.Canton.Localnet.Testing;
using Richtypes;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

[Trait("Category", "Integration")]
public class RichTypesRoundTripTests
{
    private const string GrpcUrlEnv = "CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL";
    private const string DefaultGrpcUrl = "http://localhost:11901";

    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this integration test.";

    private static readonly string[] ExpectedTags = ["alpha", "beta"];

    private static string DarPath() => Path.Combine(
        AppContext.BaseDirectory, "testdata", "richtypes", "richtypes.dar");

    private static LedgerClient NewClient(LocalnetFixture fixture, string userId)
    {
        var grpcAddress = Environment.GetEnvironmentVariable(GrpcUrlEnv) ?? DefaultGrpcUrl;
        var tokenProvider = new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync);
        return new LedgerClient(
            new LedgerClientOptions { GrpcAddress = grpcAddress, UserId = userId },
            tokenProvider);
    }

    [Fact]
    public async Task rich_record_round_trips_every_typed_field_through_the_ledger()
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
        await fixture.GrantUserRightsAsync(
            userId,
            actAs: new[] { party.PartyId },
            cancellationToken: TestContext.Current.CancellationToken);

        using var client = NewClient(fixture, userId);

        var markerOutcome = await client.CreateAsync(new Marker(owner), owner, TestContext.Current.CancellationToken);
        var markerCid = Assert.IsType<ExerciseOutcome<ContractId<Marker>>.One>(markerOutcome).Result;
        Assert.False(string.IsNullOrWhiteSpace(markerCid.Value), "created Marker ContractId is empty");

        var firstAssetOutcome = await client.CreateAsync(new Asset(owner, 100m), owner, TestContext.Current.CancellationToken);
        var firstAssetCid = Assert.IsType<ExerciseOutcome<ContractId<Asset>>.One>(firstAssetOutcome).Result;
        var secondAssetOutcome = await client.CreateAsync(new Asset(owner, 250m), owner, TestContext.Current.CancellationToken);
        var secondAssetCid = Assert.IsType<ExerciseOutcome<ContractId<Asset>>.One>(secondAssetOutcome).Result;
        var holdingCid = new ContractId<IHolding>(firstAssetCid.Value);
        var holdingCids = new[] { holdingCid, new ContractId<IHolding>(secondAssetCid.Value) };

        var observedAt = new DateTimeOffset(2026, 5, 29, 13, 30, 0, TimeSpan.Zero);
        var asOf = new DateOnly(2026, 5, 29);
        var payload = new RichRecord(
            Owner: owner,
            Count: 42L,
            Amount: 12.34m,
            Label: "initial",
            Active: true,
            AsOf: asOf,
            ObservedAt: observedAt,
            Note: "hello",
            Tags: ExpectedTags,
            Attributes: new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" },
            Marker: markerCid,
            HoldingCid: holdingCid,
            HoldingCids: holdingCids,
            Profile: new Profile(Nickname: "cdg", Level: 7L),
            Outcome: new Outcome.Win(new Outcome_Win(Prize: 250.50m, Tier: "gold")),
            Fee: 0.05m);

        var createOutcome = await client.CreateAsync(payload, owner, TestContext.Current.CancellationToken);
        var createdCid = Assert.IsType<ExerciseOutcome<ContractId<RichRecord>>.One>(createOutcome).Result;
        Assert.False(string.IsNullOrWhiteSpace(createdCid.Value), "created RichRecord ContractId is empty");

        var seen = await ReadBackAsync(client, owner, createdCid.Value);
        Assert.NotNull(seen);
        var readBack = RichRecord.FromRecord(seen!.Payload);

        Assert.Equal(party.PartyId, readBack.Owner.Id);
        Assert.Equal(42L, readBack.Count);
        Assert.Equal(12.34m, readBack.Amount);
        Assert.Equal("initial", readBack.Label);
        Assert.True(readBack.Active);
        Assert.Equal(asOf, readBack.AsOf);
        Assert.Equal(observedAt, readBack.ObservedAt);
        Assert.Equal("hello", readBack.Note);
        Assert.Equal(ExpectedTags, readBack.Tags);
        Assert.Equal("v1", readBack.Attributes["k1"]);
        Assert.Equal("v2", readBack.Attributes["k2"]);
        Assert.Equal(2, readBack.Attributes.Count);
        Assert.Equal(markerCid.Value, readBack.Marker.Value);
        Assert.Equal(holdingCid.Value, readBack.HoldingCid.Value);
        Assert.Equal(
            holdingCids.Select(cid => cid.Value),
            readBack.HoldingCids.Select(cid => cid.Value));
        Assert.Equal("cdg", readBack.Profile.Nickname);
        Assert.Equal(7L, readBack.Profile.Level);
        var win = Assert.IsType<Outcome.Win>(readBack.Outcome);
        Assert.Equal(250.50m, win.Value.Prize);
        Assert.Equal("gold", win.Value.Tier);
        Assert.Equal(0.05m, readBack.Fee);
    }

    [Fact]
    public async Task relabel_choice_creates_a_relabelled_contract()
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
        await fixture.GrantUserRightsAsync(
            userId,
            actAs: new[] { party.PartyId },
            cancellationToken: TestContext.Current.CancellationToken);

        using var client = NewClient(fixture, userId);

        var markerOutcome = await client.CreateAsync(new Marker(owner), owner, TestContext.Current.CancellationToken);
        var markerCid = Assert.IsType<ExerciseOutcome<ContractId<Marker>>.One>(markerOutcome).Result;

        var assetOutcome = await client.CreateAsync(new Asset(owner, 100m), owner, TestContext.Current.CancellationToken);
        var assetCid = Assert.IsType<ExerciseOutcome<ContractId<Asset>>.One>(assetOutcome).Result;
        var holdingCid = new ContractId<IHolding>(assetCid.Value);

        var payload = new RichRecord(
            Owner: owner,
            Count: 1L,
            Amount: 1.00m,
            Label: "initial",
            Active: true,
            AsOf: new DateOnly(2026, 5, 29),
            ObservedAt: DateTimeOffset.UnixEpoch,
            Note: null,
            Tags: Array.Empty<string>(),
            Attributes: new Dictionary<string, string>(),
            Marker: markerCid,
            HoldingCid: holdingCid,
            HoldingCids: new[] { holdingCid },
            Profile: new Profile(Nickname: "cdg", Level: 1L),
            Outcome: new Outcome.Pending(),
            Fee: 1.00m);

        var createOutcome = await client.CreateAsync(payload, owner, TestContext.Current.CancellationToken);
        var createdCid = Assert.IsType<ExerciseOutcome<ContractId<RichRecord>>.One>(createOutcome).Result;

        var relabelOutcome = await createdCid.RelabelAsync(
            client,
            new RichRecord.Relabel(NewLabel: "renamed"),
            owner,
            cancellationToken: TestContext.Current.CancellationToken);
        var relabelResult = Assert.IsType<ExerciseOutcome<RelabelResult>.One>(relabelOutcome).Result;
        var relabelledCid = relabelResult.RichRecord;
        Assert.False(string.IsNullOrWhiteSpace(relabelledCid.Value), "relabelled ContractId is empty");
        Assert.NotEqual(createdCid.Value, relabelledCid.Value);

        var seen = await ReadBackAsync(client, owner, relabelledCid.Value);
        Assert.NotNull(seen);
        var readBack = RichRecord.FromRecord(seen!.Payload);
        Assert.Equal("renamed", readBack.Label);
        Assert.IsType<Outcome.Pending>(readBack.Outcome);
        Assert.Equal(1.00m, readBack.Fee);
    }

    [Fact]
    public async Task TryExerciseAsync_exercises_the_Relabel_choice_directly_against_a_real_node()
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
        await fixture.GrantUserRightsAsync(
            userId,
            actAs: new[] { party.PartyId },
            cancellationToken: TestContext.Current.CancellationToken);

        using var client = NewClient(fixture, userId);

        var markerOutcome = await client.CreateAsync(new Marker(owner), owner, TestContext.Current.CancellationToken);
        var markerCid = Assert.IsType<ExerciseOutcome<ContractId<Marker>>.One>(markerOutcome).Result;

        var assetOutcome = await client.CreateAsync(new Asset(owner, 100m), owner, TestContext.Current.CancellationToken);
        var assetCid = Assert.IsType<ExerciseOutcome<ContractId<Asset>>.One>(assetOutcome).Result;
        var holdingCid = new ContractId<IHolding>(assetCid.Value);

        var payload = new RichRecord(
            Owner: owner,
            Count: 1L,
            Amount: 1.00m,
            Label: "before-direct-exercise",
            Active: true,
            AsOf: new DateOnly(2026, 5, 29),
            ObservedAt: DateTimeOffset.UnixEpoch,
            Note: null,
            Tags: Array.Empty<string>(),
            Attributes: new Dictionary<string, string>(),
            Marker: markerCid,
            HoldingCid: holdingCid,
            HoldingCids: new[] { holdingCid },
            Profile: new Profile(Nickname: "cdg", Level: 1L),
            Outcome: new Outcome.Pending(),
            Fee: 1.00m);

        var createOutcome = await client.CreateAsync(payload, owner, TestContext.Current.CancellationToken);
        var createdCid = Assert.IsType<ExerciseOutcome<ContractId<RichRecord>>.One>(createOutcome).Result;

        var command = new ExerciseCommand(
            RichRecord.TemplateId,
            createdCid,
            new ChoiceName("Relabel"),
            new RichRecord.Relabel(NewLabel: "renamed-direct").ToRecord());

        var exerciseOutcome = await client.TryExerciseAsync<ContractId<RichRecord>>(
            command, owner, cancellationToken: TestContext.Current.CancellationToken);

        var relabelledCid = Assert.IsType<ExerciseOutcome<ContractId<RichRecord>>.One>(exerciseOutcome).Result;
        Assert.False(string.IsNullOrWhiteSpace(relabelledCid.Value), "relabelled ContractId is empty");
        Assert.NotEqual(createdCid.Value, relabelledCid.Value);

        var seen = await ReadBackAsync(client, owner, relabelledCid.Value);
        Assert.NotNull(seen);
        var readBack = RichRecord.FromRecord(seen!.Payload);
        Assert.Equal("renamed-direct", readBack.Label);
    }

    private static async Task<AcsSnapshotEntry<RichRecord>.Created?> ReadBackAsync(
        LedgerClient client, Party owner, string contractIdValue)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await foreach (var evt in client.SubscribeActiveAsync<RichRecord>(owner, cancellationToken: cts.Token))
        {
            if (evt is AcsSnapshotEntry<RichRecord>.Created created && created.ContractId.Value == contractIdValue)
            {
                return created;
            }
        }
        return null;
    }
}
