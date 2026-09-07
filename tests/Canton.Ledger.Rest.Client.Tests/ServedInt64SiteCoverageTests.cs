// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Integration.Tests;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

/// <summary>
/// Coverage for the classifier behind the served int64 conformance guard. That guard runs against a
/// document fetched live, so a classifier that quietly answered "already reshaped" for a site nobody
/// listed would turn the one hole it exists to close back into a green run. The site names below are
/// the served document's own, which is why they carry the participant's <c>Js</c> prefixes and the
/// numeric suffixes tapir appends when one title describes several schemas.
/// </summary>
public class ServedInt64SiteCoverageTests
{
    [Theory]
    [InlineData("JsTrafficReport.consumedCost")]
    [InlineData("JsTrafficReport.offset")]
    public void SitesNeitherReshapedNorExempt_reports_an_int64_the_tables_and_the_exemption_sets_both_miss(
        string servedSite)
        => ServedInt64SiteCoverage.SitesNeitherReshapedNorExempt([servedSite]).Should().Equal(servedSite);

    [Theory]
    [InlineData("JsTransaction.paidTrafficCost")]
    [InlineData("JsAssignmentEvent.reassignmentCounter")]
    [InlineData("Completion1.offset")]
    [InlineData("OffsetCheckpoint1.offset")]
    public void SitesNeitherReshapedNorExempt_reads_a_served_schema_as_the_generated_type_it_stands_for(
        string servedSite)
        => ServedInt64SiteCoverage.SitesNeitherReshapedNorExempt([servedSite]).Should().BeEmpty();

    [Theory]
    [InlineData("GetUpdatesRequest.endInclusive")]
    [InlineData("DeduplicationOffset1.value")]
    [InlineData("Duration.seconds")]
    [InlineData("JsTransactionTree.offset")]
    public void SitesNeitherReshapedNorExempt_is_silent_about_a_site_an_exemption_set_states_a_reason_for(
        string servedSite)
        => ServedInt64SiteCoverage.SitesNeitherReshapedNorExempt([servedSite]).Should().BeEmpty();

    [Fact]
    public void ExemptedSites_names_every_site_the_classifier_lets_through_unreshaped()
        => ServedInt64SiteCoverage.ExemptedSites().Should().Contain("Duration.seconds");
}
