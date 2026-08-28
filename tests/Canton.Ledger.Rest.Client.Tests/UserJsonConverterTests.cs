// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class UserJsonConverterTests
{
    private static (IAuthenticatedUserApi Api, RecordingHttpHandler Transport) BuildApi() =>
        RestApiFactory.Build<IAuthenticatedUserApi>();

    [Fact]
    public async Task GetAuthenticatedUser_binds_the_camelCase_primaryParty_key()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(
            HttpStatusCode.OK,
            """{"user":{"id":"participant_admin","primaryParty":"operator::ns1"}}""");

        var response = await api.GetAuthenticatedUser(cancellationToken: TestContext.Current.CancellationToken);

        response.User.PrimaryParty.Should().Be("operator::ns1");
        response.User.AdditionalProperties.Should().NotContainKey("primaryParty");
    }

    [Fact]
    public async Task GetAuthenticatedUser_still_binds_the_snake_case_primary_party_key()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(
            HttpStatusCode.OK,
            """{"user":{"id":"participant_admin","primary_party":"operator::ns1"}}""");

        var response = await api.GetAuthenticatedUser(cancellationToken: TestContext.Current.CancellationToken);

        response.User.PrimaryParty.Should().Be("operator::ns1");
        response.User.AdditionalProperties.Should().NotContainKey("primary_party");
    }

    [Fact]
    public async Task GetAuthenticatedUser_preserves_the_other_User_fields_from_their_snake_case_keys()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(
            HttpStatusCode.OK,
            """{"user":{"id":"participant_admin","is_deactivated":true,"identity_provider_id":"idp-1"}}""");

        var response = await api.GetAuthenticatedUser(cancellationToken: TestContext.Current.CancellationToken);

        response.User.Id.Should().Be("participant_admin");
        response.User.IsDeactivated.Should().BeTrue();
        response.User.IdentityProviderId.Should().Be("idp-1");
    }

    [Fact]
    public async Task GetAuthenticatedUser_preserves_unknown_wire_keys_in_AdditionalProperties()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(
            HttpStatusCode.OK,
            """{"user":{"id":"participant_admin","someFutureField":"future-value"}}""");

        var response = await api.GetAuthenticatedUser(cancellationToken: TestContext.Current.CancellationToken);

        response.User.AdditionalProperties.Should().ContainKey("someFutureField");
    }

    [Fact]
    public async Task GetAuthenticatedUser_deserializes_metadata_when_present()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(
            HttpStatusCode.OK,
            """{"user":{"id":"participant_admin","metadata":{"resourceVersion":"42"}}}""");

        var response = await api.GetAuthenticatedUser(cancellationToken: TestContext.Current.CancellationToken);

        response.User.Metadata.Should().NotBeNull();
        response.User.Metadata.ResourceVersion.Should().Be("42");
    }

    [Fact]
    public void User_serializes_request_bodies_with_generated_camelCase_names_and_omits_unset_fields()
    {
        var json = JsonSerializer.Serialize(
            new User { Id = "participant_admin", PrimaryParty = "operator::ns1" },
            RestRefitSettings.SerializerOptions);

        using var body = JsonDocument.Parse(json);
        body.RootElement.GetProperty("primaryParty").GetString().Should().Be("operator::ns1");
        body.RootElement.GetProperty("id").GetString().Should().Be("participant_admin");
        body.RootElement.TryGetProperty("metadata", out _).Should()
            .BeFalse("unset optional reference fields must be omitted on the wire as proto3 JSON does");
    }
}
