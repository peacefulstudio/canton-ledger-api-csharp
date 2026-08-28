// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Peaceful.Canton.Localnet.Testing;
using Richtypes;
using Xunit;

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// LocalNet conformance coverage for <see cref="RestLedgerClient"/>'s Canton-extra read surface:
/// <c>GetUpdateByOffsetAsync</c>, <c>GetUpdateByIdAsync</c>, and <c>GetConnectedSynchronizersAsync</c>.
/// Each read is exercised against a real participant by submitting a <c>richtypes.dar</c> Marker
/// create with <c>SubmitAndWaitAsync</c> and reading the resulting update back, so the point reads
/// are asserted against a known-on-ledger update id and offset.
/// </summary>
[Trait("Category", "Integration")]
public class RestLedgerCantonReadsConformanceTests
{
    private static string DarPath() => Path.Combine(
        AppContext.BaseDirectory, "testdata", "richtypes", "richtypes.dar");

    private static async Task<Party> NewOwnerAsync(RestConformanceLane lane, CancellationToken cancellationToken)
    {
        var darOutcome = await lane.Fixture.UploadDarAsync(DarPath(), cancellationToken);
        Assert.True(
            darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
            $"Unexpected DAR upload outcome: {darOutcome}");

        var party = await lane.Fixture.AllocatePartyAsync("rest-canton-reads", cancellationToken: cancellationToken);
        await lane.Fixture.GrantUserRightsAsync(
            lane.Fixture.ValidatorUserId, actAs: [party.PartyId], cancellationToken: cancellationToken);
        return new Party(party.PartyId);
    }

    private static Task<SubmitAndWaitResult> SubmitMarkerAsync(
        RestConformanceLane lane, Party owner, CancellationToken cancellationToken) =>
        lane.LedgerClient.SubmitAndWaitAsync(
            CommandsSubmission.Single(CreateCommand.For(new Marker(owner)), owner),
            cancellationToken: cancellationToken);

    [Fact]
    public async Task GetUpdateByOffsetAsync_reads_back_the_submitted_transaction_at_its_completion_offset()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);
        var owner = await NewOwnerAsync(lane, TestContext.Current.CancellationToken);
        var submitted = await SubmitMarkerAsync(lane, owner, TestContext.Current.CancellationToken);
        var submitter = new SubmitterInfo(owner, new HashSet<Party>());

        var update = await lane.LedgerClient.GetUpdateByOffsetAsync(
            submitted.CompletionOffset.Value, submitter, TestContext.Current.CancellationToken);

        Assert.Equal(submitted.UpdateId, update.UpdateId);
        Assert.Equal(submitted.CompletionOffset.Value, update.CompletionOffset.Value);
        Assert.Contains(update.CreatedContracts, c => c.TemplateId.EntityName == "Marker");
    }

    [Fact]
    public async Task GetUpdateByIdAsync_reads_back_the_submitted_transaction_by_its_update_id()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);
        var owner = await NewOwnerAsync(lane, TestContext.Current.CancellationToken);
        var submitted = await SubmitMarkerAsync(lane, owner, TestContext.Current.CancellationToken);
        var submitter = new SubmitterInfo(owner, new HashSet<Party>());

        var update = await lane.LedgerClient.GetUpdateByIdAsync(
            submitted.UpdateId, submitter, TestContext.Current.CancellationToken);

        Assert.Equal(submitted.UpdateId, update.UpdateId);
        Assert.Equal(submitted.CompletionOffset.Value, update.CompletionOffset.Value);
        Assert.Contains(update.CreatedContracts, c => c.TemplateId.EntityName == "Marker");
    }

    [Fact]
    public async Task GetConnectedSynchronizersAsync_returns_the_single_localnet_synchronizer_for_the_party()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);
        var owner = await NewOwnerAsync(lane, TestContext.Current.CancellationToken);
        var expected = Assert.Single(
            await lane.Fixture.GetConnectedSynchronizersAsync(owner.Id, TestContext.Current.CancellationToken));

        var synchronizers = await lane.LedgerClient.GetConnectedSynchronizersAsync(
            owner, cancellationToken: TestContext.Current.CancellationToken);

        var single = Assert.Single(synchronizers);
        Assert.Equal(expected.Id, single.SynchronizerId);
        Assert.Equal(expected.Alias, single.SynchronizerAlias);
    }
}
