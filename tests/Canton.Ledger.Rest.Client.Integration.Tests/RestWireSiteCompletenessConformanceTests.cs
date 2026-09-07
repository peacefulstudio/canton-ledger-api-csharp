// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// LocalNet conformance coverage for the completeness of the two wire-site tables, which the unit
/// tier cannot establish for int64. A duration site marks itself on the generated surface — the
/// generator copies the proto3-duration pattern out of the vendored spec — so a reflection test can
/// hold <c>WireDurationSites</c> complete against the surface. An int64 site carries no such mark:
/// the vendored spec is derived from the Ledger API protobuf definitions, proto3 canonical JSON
/// encodes an int64 as a string, and the generator therefore has nothing to write down. A unit test
/// can only rediscover the type names and the json names <c>WireInt64Sites</c> already lists, so the
/// field on a type neither table names, under a name no listed site uses, stays invisible to it.
/// <para>
/// The participant's own document does carry the mark. It declares <c>format: int64</c> on every
/// property it encodes as a raw JSON number, which is the discriminator this suite holds the table
/// complete against. A site neither reshaped nor exempt makes System.Text.Json throw where the
/// generated POCO declares a string, failing the whole response rather than that one field.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public class RestWireSiteCompletenessConformanceTests(ITestOutputHelper output)
{
    private const string DurationSchemaReference = "#/components/schemas/Duration";

    [Fact]
    public async Task Int64PropertySites_are_each_reshaped_by_WireInt64Sites_or_exempt_with_a_stated_reason()
    {
        var served = await ServedSchemaAsync(TestContext.Current.CancellationToken);
        var sites = served.Int64PropertySites();

        sites.Should().NotBeEmpty(
            "the int64 universe is discovered through the format the participant's own document "
            + "declares, so a document that stops declaring formats — or a reader that stops finding "
            + "them — empties it and leaves the check below trivially satisfied. Canton 3.5.11 "
            + "declares 42 of them directly on schema properties");

        ServedInt64SiteCoverage.SitesNeitherReshapedNorExempt(sites).Should().BeEmpty(
            "the wire tables are opt-in by type — UseWireEncoding never visits a type they do not "
            + "name — so an int64 the participant sends on a type neither table names is reshaped by "
            + "nothing, and System.Text.Json throws where the generated POCO declares a string, "
            + "failing the whole response. Add the site to WireInt64Sites.ByOwner, or to whichever "
            + "exemption set in ServedInt64SiteCoverage states why it is left alone");
    }

    [Fact]
    public async Task ExemptedSites_name_only_int64_properties_the_participant_still_declares()
    {
        var served = await ServedSchemaAsync(TestContext.Current.CancellationToken);
        var declared = served.Int64PropertySites().ToHashSet(StringComparer.Ordinal);

        ServedInt64SiteCoverage.ExemptedSites().Should().OnlyContain(
            site => declared.Contains(site),
            "an exemption naming a site the participant no longer declares as an int64 is dead weight "
            + "that a later Canton release can revive against a different property of the same name, "
            + "exempting a site nobody ever decided to exempt");
    }

    [Fact]
    public async Task OffsetCheckpointFeature_serves_its_emission_delay_as_the_Duration_schema_minLedgerTimeRel_uses()
    {
        var served = await ServedSchemaAsync(TestContext.Current.CancellationToken);

        served.ReferenceTargetOf("OffsetCheckpointFeature", "maxOffsetCheckpointEmissionDelay").Should().Be(
            DurationSchemaReference,
            "the vendored spec declares this bound as a proto3-canonical duration string, and the "
            + "participant answers GET /v2/version with the seconds-and-nanos object instead. It is "
            + "in WireDurationSites on the strength of this measurement, so a release that went back "
            + "to serving the string form is what would retire that entry");

        served.ReferenceTargetOf("JsCommands", "minLedgerTimeRel").Should().Be(
            DurationSchemaReference,
            "the emission delay is reshaped because it has the same served form as the bound the "
            + "table has always reshaped. The two entries stand or fall together, so a divergence "
            + "here means one of them now needs a reason of its own");
    }

    private async Task<ServedOpenApiDocument> ServedSchemaAsync(CancellationToken cancellationToken)
    {
        await using var lane = await RestConformanceLane.OpenAsync(cancellationToken);
        using var client = lane.CreateWireLevelClient();

        var served = await ServedOpenApiDocument.FetchAsync(client, cancellationToken);
        output.WriteLine($"Participant serves its JSON Ledger API schema as version {served.CantonVersion}.");
        return served;
    }
}
