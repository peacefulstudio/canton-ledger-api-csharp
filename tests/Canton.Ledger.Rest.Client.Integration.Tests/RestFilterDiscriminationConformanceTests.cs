// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Peaceful.Canton.Localnet.Testing;
using Richtypes;
using Xunit;
using WireIdentifierFilter = Canton.Ledger.Rest.Client.Raw.IdentifierFilter;
using WireWildcardFilter = Canton.Ledger.Rest.Client.Raw.WildcardFilter;

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// LocalNet conformance coverage that a template filter we send actually restricts what the
/// participant returns. A filter the server discards is answered with a 200 and the unfiltered
/// superset its documented default produces, so every assertion here has to be discriminating:
/// one counts a filtered snapshot against a wildcard snapshot taken at the same offset for the
/// same party, the other reads a filtered snapshot through <see cref="RestLedgerClient"/> and
/// requires it to classify every entry.
/// <para>
/// The owner is seeded with one <c>Marker</c> and one <c>Asset</c>, because a party holding a
/// single template cannot distinguish a filter that was honored from one that was discarded: a
/// snapshot is scoped to the requesting party either way, so both would return that one contract.
/// Two templates are what make the discarded case observable — as a larger count on the wire, and
/// as an unclassified entry through the client.
/// </para>
/// <para>
/// The counting assertions stay at raw HTTP because their wildcard leg is a request the typed
/// client cannot express: it always builds a template filter for the streamed type.
/// </para>
/// <para>
/// The snapshot read asserts the envelope — that entries arrive, classify, and end at a
/// checkpoint — and deliberately stops short of reading a payload back into its template. A
/// contract's fields arrive as Daml-LF JSON, which carries no type information, so a party reads
/// back as text and a template's strict field casts reject it. Reconstructing those fields needs
/// a reader driven by the template's Daml type, which this client does not yet have.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public class RestFilterDiscriminationConformanceTests
{
    private const string ActiveContractsPath = "/v2/state/active-contracts";

    private static string DarPath() => Path.Combine(
        AppContext.BaseDirectory, "testdata", "richtypes", "richtypes.dar");

    private static async Task<Party> NewOwnerAsync(RestConformanceLane lane, CancellationToken cancellationToken)
    {
        var darOutcome = await lane.Fixture.UploadDarAsync(DarPath(), cancellationToken);
        Assert.True(
            darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
            $"Unexpected DAR upload outcome: {darOutcome}");

        var party = await lane.Fixture.AllocatePartyAsync("rest-filter-discrimination", cancellationToken: cancellationToken);
        await lane.Fixture.GrantUserRightsAsync(
            lane.Fixture.ValidatorUserId, actAs: [party.PartyId], cancellationToken: cancellationToken);
        return new Party(party.PartyId);
    }

    private static Task<SubmitAndWaitResult> SubmitAsync(
        RestConformanceLane lane, ICommand command, Party owner, CancellationToken cancellationToken) =>
        lane.LedgerClient.SubmitAndWaitAsync(
            CommandsSubmission.Single(command, owner), cancellationToken: cancellationToken);

    private static Task<SubmitAndWaitResult> SubmitMarkerAsync(
        RestConformanceLane lane, Party owner, CancellationToken cancellationToken) =>
        SubmitAsync(lane, CreateCommand.For(new Marker(owner)), owner, cancellationToken);

    private static Task<SubmitAndWaitResult> SubmitAssetAsync(
        RestConformanceLane lane, Party owner, CancellationToken cancellationToken) =>
        SubmitAsync(lane, CreateCommand.For(new Asset(owner, 1m)), owner, cancellationToken);

    [Fact]
    public async Task ActiveContracts_returns_strictly_fewer_contracts_for_a_template_filter_than_for_a_wildcard()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);
        var owner = await NewOwnerAsync(lane, TestContext.Current.CancellationToken);
        await SubmitMarkerAsync(lane, owner, TestContext.Current.CancellationToken);
        await SubmitAssetAsync(lane, owner, TestContext.Current.CancellationToken);
        using var wireClient = lane.CreateWireLevelClient();

        var activeAtOffset = await lane.LedgerClient.GetLedgerEndAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        var submitter = new SubmitterInfo(owner, new HashSet<Party>());
        var templateRequest = RestSubscribeRequestBuilder.BuildGetActiveContractsRequest<Marker>(
            submitter, activeAtOffset.Value);
        var wildcardRequest = RestSubscribeRequestBuilder.BuildGetActiveContractsRequest<Marker>(
            submitter, activeAtOffset.Value);
        foreach (var cumulative in wildcardRequest.EventFormat.FiltersByParty.Values.SelectMany(filters => filters.Cumulative))
        {
            cumulative.IdentifierFilter = new WireIdentifierFilter { WildcardFilter = new WireWildcardFilter() };
        }

        var wildcardCount = await CountActiveContractsAsync(
            wireClient, wildcardRequest, TestContext.Current.CancellationToken);
        var filteredCount = await CountActiveContractsAsync(
            wireClient, templateRequest, TestContext.Current.CancellationToken);

        wildcardCount.Should().BePositive();
        filteredCount.Should().BePositive();
        filteredCount.Should().BeLessThan(wildcardCount);
    }

    [Fact]
    public async Task SubscribeActiveAsync_returns_only_the_filtered_template()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);
        var owner = await NewOwnerAsync(lane, TestContext.Current.CancellationToken);
        await SubmitMarkerAsync(lane, owner, TestContext.Current.CancellationToken);
        await SubmitAssetAsync(lane, owner, TestContext.Current.CancellationToken);
        var ledgerEnd = await lane.LedgerClient.GetLedgerEndAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var entries = new List<AcsSnapshotEntry<Marker>>();
        await foreach (var entry in lane.LedgerClient.SubscribeActiveAsync<Marker>(
            owner, ledgerEnd, TestContext.Current.CancellationToken))
        {
            entries.Add(entry);
        }

        entries.OfType<AcsSnapshotEntry<Marker>.Created>().Should().NotBeEmpty();
        entries.OfType<AcsSnapshotEntry<Marker>.Unclassified>().Should().BeEmpty();
        entries[^1].Should().BeOfType<AcsSnapshotEntry<Marker>.Checkpoint>();
    }

    private static async Task<int> CountActiveContractsAsync(
        HttpClient wireClient, object request, CancellationToken cancellationToken) =>
        CountEntries(await PostAsync(wireClient, ActiveContractsPath, request, cancellationToken));

    private static async Task<string> PostAsync(
        HttpClient wireClient, string path, object request, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(request, RestRefitSettings.SerializerOptions);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await wireClient.PostAsync(new Uri(path, UriKind.Relative), content, cancellationToken);

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, payload);
        return payload;
    }

    private static int CountEntries(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        document.RootElement.ValueKind.Should().Be(
            JsonValueKind.Array,
            "the bounded stream endpoints answer with one JSON array, not newline-delimited JSON");
        return document.RootElement.GetArrayLength();
    }
}
