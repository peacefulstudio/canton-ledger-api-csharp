// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Rest.Client.Raw;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// LocalNet conformance coverage for <see cref="IInteractiveSubmissionApi"/>. The proto-derived twin
/// spells three of this route's query parameters in a way the participant does not read, and only the
/// required one fails loudly: the two optional ones are dropped in silence, so a 200 alone proves
/// nothing. Passing the synchronizer explicitly and asserting it comes back on the preference is what
/// proves the participant parsed it rather than ignored it.
/// </summary>
[Trait("Category", "Integration")]
public class RestInteractiveSubmissionConformanceTests
{
    private const string LocalnetPackageName = "splice-amulet";

    [Fact]
    public async Task GetPreferredPackageVersion_resolves_a_package_scoped_to_the_synchronizer_it_was_given()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);
        var party = await lane.Fixture.AllocatePartyAsync(
            "rest-preferred-package", cancellationToken: TestContext.Current.CancellationToken);
        await lane.Fixture.GrantUserRightsAsync(
            lane.Fixture.ValidatorUserId,
            actAs: [party.PartyId],
            cancellationToken: TestContext.Current.CancellationToken);
        var synchronizer = Assert.Single(await lane.Fixture.GetConnectedSynchronizersAsync(
            party.PartyId, TestContext.Current.CancellationToken));

        var response = await lane.Api<IInteractiveSubmissionApi>().GetPreferredPackageVersion(
            [party.PartyId],
            LocalnetPackageName,
            synchronizer.Id,
            DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken);

        Assert.NotNull(response.PackagePreference);
        Assert.Equal(LocalnetPackageName, response.PackagePreference.PackageReference.PackageName);
        Assert.Equal(synchronizer.Id, response.PackagePreference.SynchronizerId);
        Assert.False(
            string.IsNullOrWhiteSpace(response.PackagePreference.PackageReference.PackageVersion),
            "the participant resolved a preferred package with no version");
    }
}
