// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class DemoParityTests
{
    [Fact]
    public async Task QueryForPartyAsync_maps_Created_entries_to_projected_assets()
    {
        var owner = new Party("bob");
        var asset = new DemoAsset(new Party("issuer"), owner, "GOLD", 42m);
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(
                LedgerEvents.Created(
                    new ContractId<DemoAsset>("cid1"),
                    asset.ToRecord(),
                    LedgerOffset.At(1),
                    (SynchronizerId)"sync1",
                    new[] { owner }))
            .Build();

        var result = await AssetAcsQuery.QueryForPartyAsync(client, owner, TestContext.Current.CancellationToken);

        result.Should().ContainSingle();
        result[0].ContractId.Should().Be("cid1");
        result[0].Owner.Should().Be("bob");
        result[0].Name.Should().Be("GOLD");
        result[0].Amount.Should().Be(42m);
    }

    [Fact]
    public async Task QueryForPartyAsync_throws_on_Unclassified_entry()
    {
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(LedgerEvents.Unclassified<DemoAsset>(LedgerOffset.At(7), "unmapped-template"))
            .Build();

        var act = () => AssetAcsQuery.QueryForPartyAsync(client, new Party("bob"), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("offset 7").And.Contain("unmapped-template");
    }

    [Fact]
    public async Task QueryForPartyAsync_maps_Created_entries_followed_by_a_terminal_Checkpoint()
    {
        var owner = new Party("bob");
        var asset = new DemoAsset(new Party("issuer"), owner, "GOLD", 42m);
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(
                LedgerEvents.Created(
                    new ContractId<DemoAsset>("cid1"),
                    asset.ToRecord(),
                    LedgerOffset.At(1),
                    (SynchronizerId)"sync1",
                    new[] { owner }),
                LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(2)))
            .Build();

        var result = await AssetAcsQuery.QueryForPartyAsync(client, owner, TestContext.Current.CancellationToken);

        result.Should().ContainSingle();
        result[0].ContractId.Should().Be("cid1");
    }

    [Fact]
    public async Task QueryForPartyAsync_throws_on_StreamError_entry()
    {
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(LedgerEvents.StreamError<DemoAsset>(14, "snapshot transport failed"))
            .Build();

        var act = () => AssetAcsQuery.QueryForPartyAsync(client, new Party("bob"), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("14").And.Contain("snapshot transport failed");
    }
}

internal sealed record ProjectedAsset(string ContractId, string Owner, string Name, decimal Amount);

internal static class AssetAcsQuery
{
    public static async Task<IReadOnlyList<ProjectedAsset>> QueryForPartyAsync(
        ILedgerStreamer client,
        Party owner,
        CancellationToken cancellationToken)
    {
        var results = new List<ProjectedAsset>();
        await foreach (var entry in client.SubscribeActiveAsync<DemoAsset>(owner, cancellationToken: cancellationToken))
        {
            switch (entry)
            {
                case AcsSnapshotEntry<DemoAsset>.Created created:
                    var asset = DemoAsset.FromRecord(created.Payload);
                    results.Add(new ProjectedAsset(
                        (string)created.ContractId,
                        (string)asset.Owner,
                        asset.Name,
                        asset.Amount));
                    break;
                case AcsSnapshotEntry<DemoAsset>.Checkpoint:
                    break;
                case AcsSnapshotEntry<DemoAsset>.Unclassified unclassified:
                    throw new InvalidOperationException(
                        $"Unclassified ACS snapshot entry at offset {unclassified.Offset.Value}: {unclassified.Kind}");
                case AcsSnapshotEntry<DemoAsset>.StreamError streamError:
                    throw new InvalidOperationException(
                        $"ACS snapshot stream failed with status {streamError.StatusCode}: {streamError.Message}");
                default:
                    throw new InvalidOperationException($"Unexpected ACS snapshot entry: {entry}");
            }
        }

        return results;
    }
}
