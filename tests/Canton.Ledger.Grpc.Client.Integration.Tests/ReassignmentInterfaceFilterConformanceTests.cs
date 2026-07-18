// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Peaceful.Canton.Localnet.Testing;
using Richtypes;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

[Trait("Category", "Integration")]
public class ReassignmentInterfaceFilterConformanceTests
{
    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this integration test.";

    private const string SingleSyncSkipMessage =
        "Skipping: the participant reports fewer than two connected synchronizers, so this is the "
        + "single-synchronizer lane. Bring up a multi-synchronizer participant to run this "
        + "reassignment conformance test.";

    private const string PartyIdHint = "issuer";
    private const decimal AssetAmount = 100m;

    private static readonly TimeSpan ObservationTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task UnassignedEvent_arrives_on_a_wildcard_reassignment_filter()
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        await using var fixture = LocalnetFixture.FromEnvironment();
        await using var harness = ReassignmentHarness.FromFixture(fixture);
        var cancellationToken = TestContext.Current.CancellationToken;

        await harness.UploadRichTypesDarAsync(cancellationToken);

        var synchronizers = await harness.ParticipantSynchronizersAsync(cancellationToken);
        if (synchronizers.Count < 2)
        {
            Assert.Skip(SingleSyncSkipMessage);
        }

        var sourceSynchronizerId = synchronizers[0].SynchronizerId;
        var targetSynchronizerId = synchronizers[1].SynchronizerId;

        var issuer = await harness.HostPartyOnBothSynchronizersAsync(
            PartyIdHint, sourceSynchronizerId, targetSynchronizerId, cancellationToken);
        var contractId = await harness.CreateAssetAsync(
            issuer, sourceSynchronizerId, AssetAmount, cancellationToken);

        var beginExclusiveOffset = await harness.LedgerEndAsync(cancellationToken);
        await harness.UnassignAsync(
            issuer, contractId, sourceSynchronizerId, targetSynchronizerId, cancellationToken);

        var unassigned = await harness.ObserveUnassignedAsync(
            ReassignmentEventFormats.Wildcard(issuer),
            beginExclusiveOffset,
            contractId,
            ObservationTimeout,
            cancellationToken);

        Assert.NotNull(unassigned);
        Assert.Equal(contractId, unassigned.ContractId);
        Assert.Equal(sourceSynchronizerId, unassigned.Source);
        Assert.Equal(targetSynchronizerId, unassigned.Target);
    }

    [Fact]
    public async Task UnassignedEvent_interface_filter_selection_is_bounded_by_the_wildcard_selection()
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        await using var fixture = LocalnetFixture.FromEnvironment();
        await using var harness = ReassignmentHarness.FromFixture(fixture);
        var cancellationToken = TestContext.Current.CancellationToken;

        await harness.UploadRichTypesDarAsync(cancellationToken);

        var synchronizers = await harness.ParticipantSynchronizersAsync(cancellationToken);
        if (synchronizers.Count < 2)
        {
            Assert.Skip(SingleSyncSkipMessage);
        }

        var sourceSynchronizerId = synchronizers[0].SynchronizerId;
        var targetSynchronizerId = synchronizers[1].SynchronizerId;

        var issuer = await harness.HostPartyOnBothSynchronizersAsync(
            PartyIdHint, sourceSynchronizerId, targetSynchronizerId, cancellationToken);
        var contractId = await harness.CreateAssetAsync(
            issuer, sourceSynchronizerId, AssetAmount, cancellationToken);

        var beginExclusiveOffset = await harness.LedgerEndAsync(cancellationToken);
        await harness.UnassignAsync(
            issuer, contractId, sourceSynchronizerId, targetSynchronizerId, cancellationToken);

        var wildcardUnassigned = await harness.ObserveUnassignedAsync(
            ReassignmentEventFormats.Wildcard(issuer),
            beginExclusiveOffset,
            contractId,
            ObservationTimeout,
            cancellationToken);
        Assert.NotNull(wildcardUnassigned);

        var interfaceFilteredUnassigned = await harness.ObserveUnassignedAsync(
            ReassignmentEventFormats.InterfaceFilterOn<IHolding>(issuer),
            beginExclusiveOffset,
            contractId,
            ObservationTimeout,
            cancellationToken);

        Assert.NotNull(interfaceFilteredUnassigned);
        Assert.Equal(wildcardUnassigned.ContractId, interfaceFilteredUnassigned.ContractId);
        Assert.Equal(wildcardUnassigned.Source, interfaceFilteredUnassigned.Source);
        Assert.Equal(wildcardUnassigned.Target, interfaceFilteredUnassigned.Target);
    }

    [Fact]
    public async Task Reassignment_round_trip_surfaces_typed_Unassigned_and_Assigned_through_SubscribeAsync()
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        await using var fixture = LocalnetFixture.FromEnvironment();
        await using var harness = ReassignmentHarness.FromFixture(fixture);
        var cancellationToken = TestContext.Current.CancellationToken;

        await harness.UploadRichTypesDarAsync(cancellationToken);

        var synchronizers = await harness.ParticipantSynchronizersAsync(cancellationToken);
        if (synchronizers.Count < 2)
        {
            Assert.Skip(SingleSyncSkipMessage);
        }

        var sourceSynchronizerId = synchronizers[0].SynchronizerId;
        var targetSynchronizerId = synchronizers[1].SynchronizerId;

        var issuer = await harness.HostPartyOnBothSynchronizersAsync(
            PartyIdHint, sourceSynchronizerId, targetSynchronizerId, cancellationToken);
        var contractId = await harness.CreateAssetAsync(
            issuer, sourceSynchronizerId, AssetAmount, cancellationToken);

        var beginExclusiveOffset = await harness.LedgerEndAsync(cancellationToken);

        await harness.UnassignAsync(
            issuer, contractId, sourceSynchronizerId, targetSynchronizerId, cancellationToken);
        var unassigned = await harness.ObserveUnassignedAsync(
            ReassignmentEventFormats.Wildcard(issuer),
            beginExclusiveOffset,
            contractId,
            ObservationTimeout,
            cancellationToken);
        Assert.NotNull(unassigned);

        await harness.AssignAsync(
            issuer, unassigned.ReassignmentId, sourceSynchronizerId, targetSynchronizerId, cancellationToken);
        var assigned = await harness.ObserveAssignedAsync(
            ReassignmentEventFormats.Wildcard(issuer),
            beginExclusiveOffset,
            contractId,
            ObservationTimeout,
            cancellationToken);
        Assert.NotNull(assigned);

        Assert.Equal(unassigned.ReassignmentCounter, assigned.ReassignmentCounter);

        var typed = await harness.ObserveTypedReassignmentAsync<Asset>(
            issuer, beginExclusiveOffset, contractId, ObservationTimeout, cancellationToken);

        Assert.NotNull(typed.Unassigned);
        Assert.NotNull(typed.Assigned);
        Assert.Equal(contractId, typed.Unassigned.ContractId.Value);
        Assert.Equal(sourceSynchronizerId, typed.Unassigned.Source.Id);
        Assert.Equal(targetSynchronizerId, typed.Unassigned.Target.Id);
        Assert.Equal(contractId, typed.Assigned.ContractId.Value);
        Assert.Equal(targetSynchronizerId, typed.Assigned.Target.Id);
    }
}
