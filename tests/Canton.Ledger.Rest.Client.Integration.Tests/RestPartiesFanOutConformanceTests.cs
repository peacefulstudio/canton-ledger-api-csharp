// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// LocalNet conformance coverage that a multi-party read reaches the participant in the shape it
/// serves. <c>GET /v2/parties/{party}</c> carries one party in its path segment, where the gRPC
/// <c>PartyManagementService.GetParties</c> takes a repeated field and answers the whole read in one
/// call, so <see cref="PartyManagementServiceApiExtensions.GetPartiesAsync"/> issues one request per
/// party and concatenates.
/// <para>
/// The adapted leg reads two freshly allocated parties and must come back with both — the assertion
/// a single call could not satisfy before the fan-out existed. The delta leg puts the two parties on
/// the wire as the one comma-delimited segment an array path parameter would spell, and must be
/// refused: that is what makes the adapted leg rest on the fan-out rather than on the participant
/// accepting whatever it is sent, and it is the leg that starts failing the day the participant
/// serves several parties from one request, at which point the fan-out can retire.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public class RestPartiesFanOutConformanceTests
{
    private const string PartiesPath = "/v2/parties";

    [Fact]
    [Trait("Retires", nameof(PartyManagementServiceApiExtensions))]
    public async Task GetPartiesAsync_reads_two_parties_the_participant_serves_one_request_at_a_time()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);
        var first = await AllocatePartyAsync(lane, "rest-parties-fan-out-first");
        var second = await AllocatePartyAsync(lane, "rest-parties-fan-out-second");
        using var wireClient = lane.CreateWireLevelClient();

        var adapted = await lane.Api<IPartyManagementServiceApi>().GetPartiesAsync(
            [first, second], cancellationToken: TestContext.Current.CancellationToken);

        adapted.Select(details => details.Party).Should().Equal(
            [first, second],
            "two parties the participant just allocated must both come back from one typed read, in "
            + "the order they were asked for");

        var commaDelimited = await ReadPartiesSegmentAsync(
            wireClient, $"{first},{second}", TestContext.Current.CancellationToken);

        commaDelimited.Should().NotBe(
            HttpStatusCode.OK,
            "the participant serves this path segment as one party, so the single comma-delimited "
            + "segment an array path parameter would spell must be refused — the leg above rests on "
            + $"the fan-out and not on the participant tolerating anything; it answered {commaDelimited}");
    }

    private static async Task<string> AllocatePartyAsync(RestConformanceLane lane, string hint)
    {
        var party = await lane.Fixture.AllocatePartyAsync(
            hint, cancellationToken: TestContext.Current.CancellationToken);
        return party.PartyId;
    }

    private static async Task<HttpStatusCode> ReadPartiesSegmentAsync(
        HttpClient wireClient,
        string segment,
        CancellationToken cancellationToken)
    {
        using var response = await wireClient.GetAsync(
            new Uri($"{PartiesPath}/{Uri.EscapeDataString(segment)}", UriKind.Relative), cancellationToken);
        return response.StatusCode;
    }
}
