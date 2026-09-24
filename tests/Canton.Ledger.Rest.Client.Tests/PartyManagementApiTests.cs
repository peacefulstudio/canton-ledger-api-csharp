// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class PartyManagementApiTests
{
    private static (IPartyManagementApi Api, RecordingHttpHandler Transport) BuildApi() =>
        RestApiFactory.Build<IPartyManagementApi>();

    [Fact]
    public async Task ListKnownParties_spells_the_identity_provider_and_party_filter_the_way_the_participant_reads_them()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(HttpStatusCode.OK, """{"partyDetails":[]}""");

        await api.ListKnownParties(
            identityProviderId: "my-idp",
            filterParty: "alice",
            cancellationToken: TestContext.Current.CancellationToken);

        var query = transport.LastRequest!.RequestUri!.PathAndQuery;
        query.Should().Be("/v2/parties?identity-provider-id=my-idp&filter-party=alice");
        query.Should().NotContain("identityProviderId=").And.NotContain("filterParty=");
    }

    [Fact]
    public async Task ListKnownParties_omits_the_identity_provider_and_party_filter_that_were_not_given()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(HttpStatusCode.OK, """{"partyDetails":[]}""");

        await api.ListKnownParties(cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/parties");
    }

    [Fact]
    public async Task GetParties_spells_the_identity_provider_the_way_the_participant_reads_it()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(HttpStatusCode.OK, """{"partyDetails":[]}""");

        await api.GetParties("alice::ns1", "my-idp", TestContext.Current.CancellationToken);

        var query = transport.LastRequest!.RequestUri!.PathAndQuery;
        query.Should().Be("/v2/parties/alice%3A%3Ans1?identity-provider-id=my-idp");
        query.Should().NotContain("identityProviderId=");
    }

    [Fact]
    public async Task GetParties_omits_the_identity_provider_that_was_not_given()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(HttpStatusCode.OK, """{"partyDetails":[]}""");

        await api.GetParties("alice::ns1", cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/parties/alice%3A%3Ans1");
    }
}
