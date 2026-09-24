// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Rest.Client.Raw;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// LocalNet conformance coverage for <see cref="IPartyManagementApi"/>. The proto-derived twin
/// spells <c>filter-party</c> as <c>filterParty</c>, which the participant does not read, and it
/// answers 200 either way, so a 200 alone proves nothing: the participant simply ignores an
/// unrecognized query parameter rather than rejecting it. Narrowing the result to a prefix the
/// freshly allocated party matches, and to one it does not, is what proves the participant parsed
/// the parameter rather than dropped it.
/// </summary>
[Trait("Category", "Integration")]
public class RestPartyManagementConformanceTests
{
    [Fact]
    public async Task ListKnownParties_narrows_to_the_party_prefix_it_was_given()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);
        var party = await lane.Fixture.AllocatePartyAsync(
            "rest-party-filter", cancellationToken: TestContext.Current.CancellationToken);

        var filtered = await lane.Api<IPartyManagementApi>().ListKnownParties(
            filterParty: party.PartyIdHint, cancellationToken: TestContext.Current.CancellationToken);
        var filteredByAnUnrelatedPrefix = await lane.Api<IPartyManagementApi>().ListKnownParties(
            filterParty: "rest-party-filter-does-not-exist",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(filtered.PartyDetails, details => details.Party == party.PartyId);
        Assert.DoesNotContain(filteredByAnUnrelatedPrefix.PartyDetails, details => details.Party == party.PartyId);
    }
}
