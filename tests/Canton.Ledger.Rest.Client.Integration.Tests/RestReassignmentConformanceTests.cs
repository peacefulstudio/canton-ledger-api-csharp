// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Richtypes;
using Xunit;

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// LocalNet conformance coverage for <see cref="RestLedgerClient"/>'s reassignment write surface on
/// the multi-synchronizer lane: a real unassign source→assign target round trip of an
/// <see cref="Asset"/> over both
/// <see cref="Canton.Ledger.Abstractions.ICantonLedgerClient.SubmitReassignmentAsync"/> (fire) and
/// <see cref="Canton.Ledger.Abstractions.ICantonLedgerClient.TrySubmitAndWaitForReassignmentAsync{T}"/>
/// (submit-and-wait). Green execution requires the multi-sync bootstrap; on any lane that
/// cannot satisfy it — no LocalNet, a single synchronizer, a submitter hosted on one synchronizer,
/// or the reassignment feature flag disabled — the test skips through
/// <see cref="RestReassignmentHarness"/> rather than failing.
/// </summary>
[Trait("Category", "Integration")]
public class RestReassignmentConformanceTests
{
    private const string PartyIdHint = "rest-reassignment-issuer";
    private const decimal AssetAmount = 100m;

    private static readonly TimeSpan ObservationTimeout = TimeSpan.FromSeconds(30);

    private static string DarPath() => Path.Combine(
        AppContext.BaseDirectory, "testdata", "richtypes", "richtypes.dar");

    [Fact]
    public async Task SubmitReassignmentAsync_round_trips_an_Asset_from_source_to_target()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await RestConformanceLane.OpenAsync(cancellationToken);
        var harness = new RestReassignmentHarness(lane);

        var synchronizers = await harness.DiscoverSynchronizerPairAsync(cancellationToken);
        await harness.VetRichTypesDarOnBothAsync(DarPath(), synchronizers, cancellationToken);

        var issuer = await harness.HostPartyOnBothSynchronizersAsync(PartyIdHint, synchronizers, cancellationToken);
        var contractId = await harness.CreateAssetAsync(
            issuer, synchronizers.Source, AssetAmount, cancellationToken);

        var beginExclusiveOffset = await harness.LedgerEndAsync(cancellationToken);

        await harness.UnassignAsync(issuer, contractId, synchronizers, cancellationToken);
        var unassigned = await harness.ObserveUnassignedAsync(
            issuer, beginExclusiveOffset, contractId, ObservationTimeout, cancellationToken);

        Assert.NotNull(unassigned);
        Assert.Equal(contractId, unassigned.ContractId.Value);
        Assert.Equal(synchronizers.Source.Id, unassigned.Source.Id);
        Assert.Equal(synchronizers.Target.Id, unassigned.Target.Id);

        await harness.AssignAsync(issuer, unassigned.ReassignmentId, synchronizers, cancellationToken);
        var afterAssignOffset = await harness.LedgerEndAsync(cancellationToken);
        var assigned = await harness.ObserveAssignedAsync(
            issuer,
            afterAssignOffset > beginExclusiveOffset ? afterAssignOffset - 1 : beginExclusiveOffset,
            contractId,
            ObservationTimeout,
            cancellationToken);

        Assert.NotNull(assigned);
        Assert.Equal(contractId, assigned.ContractId.Value);
        Assert.Equal(synchronizers.Target.Id, assigned.Target.Id);
        Assert.Equal(unassigned.ReassignmentCounter, assigned.ReassignmentCounter);
    }

    [Fact]
    public async Task TrySubmitAndWaitForReassignmentAsync_round_trips_an_Asset_from_source_to_target()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await RestConformanceLane.OpenAsync(cancellationToken);
        var harness = new RestReassignmentHarness(lane);

        var synchronizers = await harness.DiscoverSynchronizerPairAsync(cancellationToken);
        await harness.VetRichTypesDarOnBothAsync(DarPath(), synchronizers, cancellationToken);

        var issuer = await harness.HostPartyOnBothSynchronizersAsync(PartyIdHint, synchronizers, cancellationToken);
        var contractId = await harness.CreateAssetAsync(
            issuer, synchronizers.Source, AssetAmount, cancellationToken);

        var unassigned = await harness.SubmitAndWaitUnassignAsync(
            issuer, contractId, synchronizers, cancellationToken);

        Assert.Equal(contractId, unassigned.ContractId.Value);
        Assert.Equal(synchronizers.Source.Id, unassigned.Source.Id);
        Assert.Equal(synchronizers.Target.Id, unassigned.Target.Id);
        Assert.False(string.IsNullOrEmpty(unassigned.ReassignmentId), "unassigned event carried no reassignment id");

        var assigned = await harness.SubmitAndWaitAssignAsync(
            issuer, unassigned.ReassignmentId, synchronizers, cancellationToken);

        Assert.Equal(contractId, assigned.ContractId.Value);
        Assert.Equal(synchronizers.Target.Id, assigned.Target.Id);
        Assert.Equal(unassigned.ReassignmentCounter, assigned.ReassignmentCounter);
    }
}
