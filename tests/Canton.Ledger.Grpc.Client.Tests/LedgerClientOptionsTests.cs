// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using FluentAssertions;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerClientOptionsTests
{
    [Fact]
    public void default_values_are_set_correctly()
    {
        var options = new LedgerClientOptions
        {
            GrpcAddress = "https://localhost:5001"
        };

        options.UserId.Should().BeNull();
        options.MaxMessageSize.Should().Be(100 * 1024 * 1024);
        options.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void grpc_address_is_required()
    {
        var options = new LedgerClientOptions
        {
            GrpcAddress = "https://participant.example.com:5001"
        };

        options.GrpcAddress.Should().Be("https://participant.example.com:5001");
    }
}
