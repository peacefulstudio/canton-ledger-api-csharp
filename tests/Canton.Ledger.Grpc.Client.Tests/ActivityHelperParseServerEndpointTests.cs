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
}
