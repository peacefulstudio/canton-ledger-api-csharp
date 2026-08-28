// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class ListKnownPartiesResponseJsonConverterTests
{
    private static (IPartyManagementServiceApi Api, RecordingHttpHandler Transport) BuildApi() =>
        RestApiFactory.Build<IPartyManagementServiceApi>();

    [Fact]
    public async Task ListKnownParties_binds_the_camelCase_partyDetails_and_nextPageToken_keys()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(
            HttpStatusCode.OK,
            """{"partyDetails":[{"party":"alice::ns1"}],"nextPageToken":"next-token"}""");

        var response = await api.ListKnownParties(
            pageToken: null!,
            pageSize: null,
            identityProviderId: null!,
            filterParty: null!,
            TestContext.Current.CancellationToken);

        response.PartyDetails.Should().ContainSingle().Which.Party.Should().Be("alice::ns1");
        response.NextPageToken.Should().Be("next-token");
    }

    [Fact]
    public async Task ListKnownParties_still_binds_the_snake_case_party_details_and_next_page_token_keys()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(
            HttpStatusCode.OK,
            """{"party_details":[{"party":"alice::ns1"}],"next_page_token":"next-token"}""");

        var response = await api.ListKnownParties(
            pageToken: null!,
            pageSize: null,
            identityProviderId: null!,
            filterParty: null!,
            TestContext.Current.CancellationToken);

        response.PartyDetails.Should().ContainSingle().Which.Party.Should().Be("alice::ns1");
        response.NextPageToken.Should().Be("next-token");
    }

    [Fact]
    public async Task ListKnownParties_returns_null_typed_properties_when_the_wire_omits_both_keys()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(HttpStatusCode.OK, "{}");

        var response = await api.ListKnownParties(
            pageToken: null!,
            pageSize: null,
            identityProviderId: null!,
            filterParty: null!,
            TestContext.Current.CancellationToken);

        response.PartyDetails.Should().BeNull();
        response.NextPageToken.Should().BeNull();
    }

    [Fact]
    public async Task ListKnownParties_preserves_unknown_wire_keys_in_AdditionalProperties()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(
            HttpStatusCode.OK,
            """{"partyDetails":[],"someFutureField":"future-value"}""");

        var response = await api.ListKnownParties(
            pageToken: null!,
            pageSize: null,
            identityProviderId: null!,
            filterParty: null!,
            TestContext.Current.CancellationToken);

        response.AdditionalProperties.Should().ContainKey("someFutureField");
    }
}
