// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class PartyManagementServiceApiExtensionsTests
{
    private const string Alice = "alice::ns1";
    private const string Bob = "bob::ns2";

    private static string Details(params string[] parties) =>
        $$"""{"partyDetails":[{{string.Join(",", parties.Select(party => $$"""{"party":"{{party}}"}"""))}}]}""";

    private static (IPartyManagementServiceApi Api, RecordingHttpHandler Transport) BuildApi() =>
        RestApiFactory.Build<IPartyManagementServiceApi>();

    [Fact]
    public async Task GetPartiesAsync_issues_one_request_per_party()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(HttpStatusCode.OK, Details());

        await api.GetPartiesAsync([Alice, Bob], cancellationToken: TestContext.Current.CancellationToken);

        transport.Requests.Select(request => request.PathAndQuery).Should().Equal(
            "/v2/parties/alice%3A%3Ans1",
            "/v2/parties/bob%3A%3Ans2");
    }

    [Fact]
    public async Task GetPartiesAsync_concatenates_the_details_of_every_response_in_the_order_asked_for()
    {
        var (api, transport) = BuildApi();
        transport
            .WithResponseForPath("/v2/parties/alice%3A%3Ans1", HttpStatusCode.OK, Details(Alice))
            .WithResponseForPath("/v2/parties/bob%3A%3Ans2", HttpStatusCode.OK, Details(Bob));

        var details = await api.GetPartiesAsync(
            [Alice, Bob], cancellationToken: TestContext.Current.CancellationToken);

        details.Select(party => party.Party).Should().Equal(Alice, Bob);
    }

    [Fact]
    public async Task GetPartiesAsync_contributes_nothing_for_a_party_the_participant_does_not_know()
    {
        var (api, transport) = BuildApi();
        transport
            .WithResponseForPath("/v2/parties/alice%3A%3Ans1", HttpStatusCode.OK, Details(Alice))
            .WithResponseForPath("/v2/parties/bob%3A%3Ans2", HttpStatusCode.OK, Details());

        var details = await api.GetPartiesAsync(
            [Alice, Bob], cancellationToken: TestContext.Current.CancellationToken);

        details.Select(party => party.Party).Should().Equal(Alice);
    }

    [Fact]
    public async Task GetPartiesAsync_carries_the_identity_provider_on_every_request()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(HttpStatusCode.OK, Details());

        await api.GetPartiesAsync(
            [Alice, Bob], identityProviderId: "idp-1", TestContext.Current.CancellationToken);

        transport.Requests.Select(request => request.PathAndQuery).Should().AllSatisfy(
            path => path.Should().EndWith("?identityProviderId=idp-1"));
    }

    [Fact]
    public async Task GetPartiesAsync_omits_the_identity_provider_that_was_not_given()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(HttpStatusCode.OK, Details());

        await api.GetPartiesAsync([Alice], cancellationToken: TestContext.Current.CancellationToken);

        transport.Requests.Single().PathAndQuery.Should().Be("/v2/parties/alice%3A%3Ans1");
    }

    [Fact]
    public async Task GetPartiesAsync_answers_an_empty_sequence_without_asking_the_participant()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(HttpStatusCode.OK, Details());

        var details = await api.GetPartiesAsync([], cancellationToken: TestContext.Current.CancellationToken);

        details.Should().BeEmpty();
        transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public void GetPartiesAsync_rejects_a_null_parties_synchronously()
    {
        var (api, _) = BuildApi();

        var act = () => { _ = api.GetPartiesAsync(null!); };

        act.Should().Throw<ArgumentNullException>(
                "a null argument must be diagnosed before any request leaves")
            .Which.ParamName.Should().Be("parties");
    }

    [Fact]
    public void GetPartiesAsync_rejects_a_null_api_synchronously()
    {
        IPartyManagementServiceApi api = null!;

        var act = () => { _ = api.GetPartiesAsync(["alice::ns"]); };

        act.Should().Throw<ArgumentNullException>(
                "a null api must be diagnosed before any request leaves")
            .Which.ParamName.Should().Be("api");
    }
}
