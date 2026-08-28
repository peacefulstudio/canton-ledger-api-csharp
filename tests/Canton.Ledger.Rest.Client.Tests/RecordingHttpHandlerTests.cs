// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class RecordingHttpHandlerTests
{
    [Fact]
    public async Task WithResponse_replaces_a_binary_body_configured_earlier()
    {
        var transport = new RecordingHttpHandler()
            .WithBinaryResponse(HttpStatusCode.OK, [0x01, 0x02], "application/octet-stream")
            .WithResponse(HttpStatusCode.OK, """{"cause":"json"}""");
        using var client = new HttpClient(transport) { BaseAddress = new Uri("http://localhost:7575") };

        using var response = await client.GetAsync("/probe", TestContext.Current.CancellationToken);

        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Be("""{"cause":"json"}""");
    }
}
