// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Refit;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class PackageApiTests
{
    private const string PackageId = "e7b1a0f4c2";
    private const string PackageHash = "1220f4a1b2c3";
    private static readonly byte[] ArchiveBytes = [0x0A, 0x14, 0x00, 0xFF, 0x80, 0x7F];

    private static (IPackageApi Api, RecordingHttpHandler Transport) BuildApi()
    {
        var (api, transport) = RestApiFactory.Build<IPackageApi>();
        transport
            .WithBinaryResponse(HttpStatusCode.OK, ArchiveBytes, "application/octet-stream")
            .WithResponseHeader(IPackageApi.PackageHashHeader, PackageHash);
        return (api, transport);
    }

    [Fact]
    public async Task GetPackage_asks_the_participant_for_the_archive_as_an_octet_stream()
    {
        var (api, transport) = BuildApi();

        using var response = await api.GetPackage(PackageId, TestContext.Current.CancellationToken);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be($"/v2/packages/{PackageId}");
        transport.LastRequest.Headers.Accept.Should().ContainSingle()
            .Which.MediaType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task GetPackage_reads_the_raw_archive_bytes_off_the_response_body()
    {
        var (api, transport) = BuildApi();

        using var response = await api.GetPackage(PackageId, TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.Should().BeTrue();
        using var archive = new MemoryStream();
        await response.Content!.CopyToAsync(archive, TestContext.Current.CancellationToken);
        archive.ToArray().Should().Equal(ArchiveBytes);
    }

    [Fact]
    public async Task GetPackage_reads_the_archive_hash_off_the_Canton_Package_Hash_response_header()
    {
        var (api, transport) = BuildApi();

        using var response = await api.GetPackage(PackageId, TestContext.Current.CancellationToken);

        response.Headers!.GetValues(IPackageApi.PackageHashHeader).Should().Equal([PackageHash]);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetPackage_returns_a_non_success_status_as_a_value_rather_than_throwing(
        HttpStatusCode status)
    {
        var (api, transport) = BuildApi();
        transport.WithBinaryResponse(status, [], "text/plain");

        using var response = await api.GetPackage(PackageId, TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.Should().BeFalse();
        response.StatusCode.Should().Be(status);
        response.Error.Should().BeOfType<ApiException>().Which.StatusCode.Should().Be(status);
    }
}
