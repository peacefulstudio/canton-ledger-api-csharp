// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class ActivityHelperParseServerEndpointTests
{
    [Theory]
    [InlineData("https://localhost:5001", "localhost", 5001)]
    [InlineData("http://ledger.example:9090", "ledger.example", 9090)]
    [InlineData("https://localhost", "localhost", 443)]
    [InlineData("http://localhost", "localhost", 80)]
    public void ParseServerEndpoint_splits_host_and_port_deriving_scheme_default_when_absent(
        string grpcAddress, string expectedAddress, int expectedPort)
    {
        var (address, port) = ActivityHelper.ParseServerEndpoint(grpcAddress);

        address.Should().Be(expectedAddress);
        port.Should().Be(expectedPort);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("file:///relative/path")]
    [InlineData("file://host/path")]
    [InlineData("ftp://host:21")]
    [InlineData("unix:///tmp/socket")]
    [InlineData("localhost:5001")]
    [InlineData("localhost")]
    public void ParseServerEndpoint_rejects_endpoints_that_are_not_absolute_http_urls(string endpoint)
    {
        var act = () => ActivityHelper.ParseServerEndpoint(endpoint);

        act.Should().Throw<ArgumentException>(
                "a malformed GrpcAddress must be rejected loudly, not silently parsed into an empty host")
            .WithParameterName("grpcAddress");
    }

    [Theory]
    [InlineData("http://")]
    [InlineData("https://")]
    public void ParseServerEndpoint_rejects_absolute_http_urls_with_an_empty_host(string endpoint)
    {
        var act = () => ActivityHelper.ParseServerEndpoint(endpoint);

        act.Should().Throw<ArgumentException>(
                "an http(s) URL with no host still parses but must not be silently accepted as an empty endpoint")
            .WithParameterName("grpcAddress");
    }
}
