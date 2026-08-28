// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Refit;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class DarApiTests
{
    private static readonly byte[] DarBytes = [0x50, 0x4B, 0x03, 0x04, 0x00, 0xFF, 0x80, 0x7F];

    private static MemoryStream Dar() => new(DarBytes);

    private static (IDarApi Api, RecordingHttpHandler Transport) BuildApi()
    {
        var (api, transport) = RestApiFactory.Build<IDarApi>();
        transport.WithResponse(HttpStatusCode.OK);
        return (api, transport);
    }

    [Fact]
    public async Task UploadDar_posts_the_raw_dar_bytes_to_v2_dars_as_an_octet_stream()
    {
        var (api, transport) = BuildApi();

        await api.UploadDar(Dar(), cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/dars");
        transport.LastRequest.Content!.Headers.ContentType!.MediaType.Should().Be("application/octet-stream");
        transport.LastRequestBytes.Should().Equal(DarBytes);
    }

    [Fact]
    public async Task UploadDar_carries_the_vetting_and_synchronizer_choices_as_query_parameters()
    {
        var (api, transport) = BuildApi();

        await api.UploadDar(
            Dar(),
            vetAllPackages: true,
            synchronizerId: "sync::ns1",
            TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery
            .Should().Be("/v2/dars?vetAllPackages=true&synchronizerId=sync%3A%3Ans1");
    }

    [Fact]
    public async Task UploadDar_omits_the_query_parameters_that_were_not_given()
    {
        var (api, transport) = BuildApi();

        await api.UploadDar(Dar(), vetAllPackages: false, cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/dars?vetAllPackages=false");
    }

    [Fact]
    public async Task ValidateDar_posts_the_raw_dar_bytes_to_v2_dars_validate_as_an_octet_stream()
    {
        var (api, transport) = BuildApi();

        await api.ValidateDar(Dar(), "sync::ns1", TestContext.Current.CancellationToken);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/dars/validate?synchronizerId=sync%3A%3Ans1");
        transport.LastRequest.Content!.Headers.ContentType!.MediaType.Should().Be("application/octet-stream");
        transport.LastRequestBytes.Should().Equal(DarBytes);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task UploadDar_throws_ApiException_carrying_the_status_on_a_non_success_response(
        HttpStatusCode status)
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(status, """{"cause":"rejected"}""");

        var act = () => api.UploadDar(Dar(), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ApiException>()).Which.StatusCode.Should().Be(status);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ValidateDar_throws_ApiException_carrying_the_status_on_a_non_success_response(
        HttpStatusCode status)
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(status, """{"cause":"rejected"}""");

        var act = () => api.ValidateDar(Dar(), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ApiException>()).Which.StatusCode.Should().Be(status);
    }
}
