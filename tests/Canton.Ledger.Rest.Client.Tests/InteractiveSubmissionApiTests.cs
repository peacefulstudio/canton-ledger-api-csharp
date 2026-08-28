// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Refit;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class InteractiveSubmissionApiTests
{
    private const string PreferredPackageVersionBody =
        """{"packagePreference":{"packageReference":{"packageId":"pkg-1","packageName":"holding","packageVersion":"1.0.0"},"synchronizerId":"sync::ns1"}}""";

    private static readonly DateTimeOffset VettingValidAt =
        new(2026, 8, 14, 9, 30, 0, TimeSpan.Zero);

    private static (IInteractiveSubmissionApi Api, RecordingHttpHandler Transport) BuildApi()
    {
        var (api, transport) = RestApiFactory.Build<IInteractiveSubmissionApi>();
        transport.WithResponse(HttpStatusCode.OK, PreferredPackageVersionBody);
        return (api, transport);
    }

    [Fact]
    public async Task GetPreferredPackageVersion_spells_every_query_parameter_the_way_the_participant_reads_it()
    {
        var (api, transport) = BuildApi();

        await api.GetPreferredPackageVersion(
            ["alice::ns1"],
            "holding",
            "sync::ns1",
            VettingValidAt,
            TestContext.Current.CancellationToken);

        var query = transport.LastRequest!.RequestUri!.PathAndQuery;
        query.Should().StartWith("/v2/interactive-submission/preferred-package-version?");
        query.Should().Contain("parties=alice%3A%3Ans1");
        query.Should().Contain("package-name=holding");
        query.Should().Contain("synchronizer-id=sync%3A%3Ans1");
        query.Should().Contain("vetting_valid_at=2026-08-14T09%3A30%3A00.0000000%2B00%3A00");
        query.Should().NotContain("packageName=").And.NotContain("synchronizerId=").And.NotContain("vettingValidAt=");
    }

    [Fact]
    public async Task GetPreferredPackageVersion_omits_vetting_valid_at_when_no_timestamp_is_given()
    {
        var (api, transport) = BuildApi();

        await api.GetPreferredPackageVersion(
            ["alice::ns1"],
            "holding",
            "sync::ns1",
            null,
            TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be(
            "/v2/interactive-submission/preferred-package-version"
            + "?parties=alice%3A%3Ans1&package-name=holding&synchronizer-id=sync%3A%3Ans1");
    }

    [Fact]
    public async Task GetPreferredPackageVersion_omits_synchronizer_id_when_no_synchronizer_is_given()
    {
        var (api, transport) = BuildApi();

        await api.GetPreferredPackageVersion(
            ["alice::ns1"],
            "holding",
            null,
            VettingValidAt,
            TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be(
            "/v2/interactive-submission/preferred-package-version"
            + "?parties=alice%3A%3Ans1&package-name=holding"
            + "&vetting_valid_at=2026-08-14T09%3A30%3A00.0000000%2B00%3A00");
    }

    [Fact]
    public async Task GetPreferredPackageVersion_repeats_the_parties_parameter_once_per_party()
    {
        var (api, transport) = BuildApi();

        await api.GetPreferredPackageVersion(
            ["alice::ns1", "bob::ns1"],
            "holding",
            "sync::ns1",
            null,
            TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be(
            "/v2/interactive-submission/preferred-package-version"
            + "?parties=alice%3A%3Ans1&parties=bob%3A%3Ans1&package-name=holding&synchronizer-id=sync%3A%3Ans1");
    }

    [Fact]
    public async Task GetPreferredPackageVersion_reads_the_package_preference_off_the_response()
    {
        var (api, transport) = BuildApi();

        var response = await api.GetPreferredPackageVersion(
            ["alice::ns1"],
            "holding",
            "sync::ns1",
            null,
            TestContext.Current.CancellationToken);

        response.PackagePreference.PackageReference.PackageId.Should().Be("pkg-1");
        response.PackagePreference.SynchronizerId.Should().Be("sync::ns1");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetPreferredPackageVersion_throws_ApiException_carrying_the_status_on_a_non_success_response(
        HttpStatusCode status)
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(status, """{"cause":"rejected"}""");

        var act = () => api.GetPreferredPackageVersion(
            ["alice::ns1"],
            "holding",
            "sync::ns1",
            null,
            TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ApiException>()).Which.StatusCode.Should().Be(status);
    }
}
