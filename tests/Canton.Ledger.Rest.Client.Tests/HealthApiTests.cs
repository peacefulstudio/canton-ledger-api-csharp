// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class HealthApiTests
{
    private static (IHealthApi Api, RecordingHttpHandler Transport) BuildApi() =>
        RestApiFactory.Build<IHealthApi>();

    [Fact]
    public async Task CheckReadiness_returns_503_not_ready_as_a_value_instead_of_throwing()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(HttpStatusCode.ServiceUnavailable, "not ready");

        var response = await api.CheckReadiness(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.IsSuccessStatusCode.Should().BeFalse();
        transport.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/readyz");
    }

    [Fact]
    public async Task CheckReadiness_returns_success_status_when_ready()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(HttpStatusCode.OK, "ready");

        var response = await api.CheckReadiness(TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task CheckLiveness_targets_livez_and_returns_the_status_as_a_value()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(HttpStatusCode.OK, "alive");

        var response = await api.CheckLiveness(TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.Should().BeTrue();
        transport.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/livez");
    }
}
