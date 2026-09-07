// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class FeaturesDescriptorSerializationTests
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
    public async Task GetLedgerApiVersion_preserves_unknown_wire_keys_in_AdditionalProperties()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(
            HttpStatusCode.OK,
            """{"version":"1.0.0","features":{"someFutureFeature":{"supported":true}}}""");

        var response = await api.GetLedgerApiVersion(TestContext.Current.CancellationToken);

        response.Features.AdditionalProperties.Should().ContainKey("someFutureFeature");
    }

    [Fact]
    public async Task GetLedgerApiVersion_binds_the_camelCase_partyManagement_offsetCheckpoint_and_packageFeature_keys()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(
            HttpStatusCode.OK,
            """
            {"version":"1.0.0","features":{
              "partyManagement":{"maxPartiesPageSize":10000},
              "offsetCheckpoint":{"maxOffsetCheckpointEmissionDelay":{"seconds":75,"nanos":0}},
              "packageFeature":{"maxVettedPackagesPageSize":100}}}
            """);

        var response = await api.GetLedgerApiVersion(TestContext.Current.CancellationToken);

        response.Features.PartyManagement.Should().NotBeNull();
        response.Features.PartyManagement.MaxPartiesPageSize.Should().Be(10000);
        response.Features.OffsetCheckpoint.Should().NotBeNull();
        response.Features.OffsetCheckpoint.MaxOffsetCheckpointEmissionDelay.Should().Be("75s");
        response.Features.PackageFeature.Should().NotBeNull();
        response.Features.PackageFeature.MaxVettedPackagesPageSize.Should().Be(100);
        response.Features.AdditionalProperties.Should()
            .NotContainKey("partyManagement").And
            .NotContainKey("offsetCheckpoint").And
            .NotContainKey("packageFeature");
    }
}
