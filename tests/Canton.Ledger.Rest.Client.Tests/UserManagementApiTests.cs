// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class UserManagementApiTests
{
    private static (IUserManagementApi Api, RecordingHttpHandler Transport) BuildApi() =>
        RestApiFactory.Build<IUserManagementApi>();

    [Fact]
    public async Task GetUser_spells_the_identity_provider_the_way_the_participant_reads_it()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(HttpStatusCode.OK, """{"user":{"id":"alice"}}""");

        await api.GetUser("alice", "my-idp", TestContext.Current.CancellationToken);

        var query = transport.LastRequest!.RequestUri!.PathAndQuery;
        query.Should().Be("/v2/users/alice?identity-provider-id=my-idp");
        query.Should().NotContain("identityProviderId=");
    }

    [Fact]
    public async Task GetUser_omits_the_identity_provider_that_was_not_given()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(HttpStatusCode.OK, """{"user":{"id":"alice"}}""");

        await api.GetUser("alice", cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/users/alice");
    }

    [Fact]
    public async Task ListUsers_sends_no_identity_provider_parameter_under_either_spelling()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(HttpStatusCode.OK, """{"users":[]}""");

        await api.ListUsers(cancellationToken: TestContext.Current.CancellationToken);

        var query = transport.LastRequest!.RequestUri!.PathAndQuery;
        query.Should().Be("/v2/users");
        query.Should().NotContain("identityProviderId=").And.NotContain("identity-provider-id=");
    }

    [Fact]
    public async Task DeleteUser_sends_no_identity_provider_parameter_under_either_spelling()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(HttpStatusCode.OK, "{}");

        await api.DeleteUser("alice", TestContext.Current.CancellationToken);

        var query = transport.LastRequest!.RequestUri!.PathAndQuery;
        query.Should().Be("/v2/users/alice");
        query.Should().NotContain("identityProviderId=").And.NotContain("identity-provider-id=");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Delete);
    }

    [Fact]
    public async Task ListUserRights_sends_no_identity_provider_parameter_under_either_spelling()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(HttpStatusCode.OK, """{"rights":[]}""");

        await api.ListUserRights("alice", TestContext.Current.CancellationToken);

        var query = transport.LastRequest!.RequestUri!.PathAndQuery;
        query.Should().Be("/v2/users/alice/rights");
        query.Should().NotContain("identityProviderId=").And.NotContain("identity-provider-id=");
    }
}
