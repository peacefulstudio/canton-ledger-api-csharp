// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class FeaturesDescriptorJsonConverterTests
{
    private static (IVersionServiceApi Api, RecordingHttpHandler Transport) BuildApi() =>
        RestApiFactory.Build<IVersionServiceApi>();

    [Fact]
    public async Task GetLedgerApiVersion_binds_the_camelCase_userManagement_key()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(
            HttpStatusCode.OK,
            """{"version":"1.0.0","features":{"userManagement":{"supported":true}}}""");

        var response = await api.GetLedgerApiVersion(TestContext.Current.CancellationToken);

        response.Features.UserManagement.Should().NotBeNull();
        response.Features.UserManagement.Supported.Should().BeTrue();
        response.Features.AdditionalProperties.Should().NotContainKey("userManagement");
    }

    [Fact]
    public async Task GetLedgerApiVersion_still_binds_the_snake_case_user_management_key()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(
            HttpStatusCode.OK,
            """{"version":"1.0.0","features":{"user_management":{"supported":true}}}""");

        var response = await api.GetLedgerApiVersion(TestContext.Current.CancellationToken);

        response.Features.UserManagement.Should().NotBeNull();
        response.Features.UserManagement.Supported.Should().BeTrue();
        response.Features.AdditionalProperties.Should().NotContainKey("user_management");
    }

    [Fact]
    public async Task GetLedgerApiVersion_preserves_the_other_Features_fields_from_their_snake_case_keys()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(
            HttpStatusCode.OK,
            """{"version":"1.0.0","features":{"experimental":{},"package_feature":{"maxPackagesPageSize":42}}}""");

        var response = await api.GetLedgerApiVersion(TestContext.Current.CancellationToken);

        response.Features.Experimental.Should().NotBeNull();
        response.Features.PackageFeature.Should().NotBeNull();
    }

    [Fact]
    public async Task GetLedgerApiVersion_preserves_unknown_wire_keys_in_AdditionalProperties()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(
            HttpStatusCode.OK,
            """{"version":"1.0.0","features":{"someFutureFeature":{"supported":true}}}""");

        var response = await api.GetLedgerApiVersion(TestContext.Current.CancellationToken);

        response.Features.AdditionalProperties.Should().ContainKey("someFutureFeature");
    }
}
