// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using System.Text.Json;
using Canton.Ledger.Pqs.Client;
using FluentAssertions;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class PqsClientOptionsTests
{
    [Fact]
    public void json_serializer_options_defaults_to_null()
    {
        var options = new PqsClientOptions { ConnectionString = "Host=localhost" };

        options.JsonSerializerOptions.Should().BeNull();
    }

    [Fact]
    public void json_serializer_options_can_be_set()
    {
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var options = new PqsClientOptions
        {
            ConnectionString = "Host=localhost",
            JsonSerializerOptions = jsonOptions
        };

        options.JsonSerializerOptions.Should().BeSameAs(jsonOptions);
    }
}
