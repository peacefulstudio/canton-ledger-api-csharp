// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Refit;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class AuthenticatedUserApiTests
{
    private static (IAuthenticatedUserApi Api, RecordingHttpHandler Transport) BuildApi() =>
        RestApiFactory.Build<IAuthenticatedUserApi>();

    [Fact]
    public async Task GetAuthenticatedUser_reads_the_user_from_v2_authenticated_user()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(
            HttpStatusCode.OK,
            """{"user":{"id":"participant_admin","primaryParty":"operator::ns1"}}""");

        var response = await api.GetAuthenticatedUser(cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/authenticated-user");
        response.User.Id.Should().Be("participant_admin");
        response.User.PrimaryParty.Should().Be("operator::ns1");
    }

    [Fact]
    public async Task GetAuthenticatedUser_scopes_to_an_identity_provider_when_one_is_given()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(HttpStatusCode.OK, """{"user":{"id":"participant_admin"}}""");

        await api.GetAuthenticatedUser("my-idp", TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery
            .Should().Be("/v2/authenticated-user?identity-provider-id=my-idp");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetAuthenticatedUser_throws_ApiException_carrying_the_status_on_a_non_success_response(
        HttpStatusCode status)
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(status, """{"cause":"denied"}""");

        var act = () => api.GetAuthenticatedUser(cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ApiException>())
            .Which.StatusCode.Should().Be(status);
    }
}
