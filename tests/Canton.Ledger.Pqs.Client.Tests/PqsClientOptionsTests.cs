// Copyright © 2026 Peaceful Studio OÜ. All rights reserved.

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class PqsClientOptionsTests
{
    [Fact]
    public void JsonSerializerOptions_defaults_to_null()
    {
        var options = new PqsClientOptions { ConnectionString = "Host=localhost" };

        options.JsonSerializerOptions.Should().BeNull();
    }

    [Fact]
    public void JsonSerializerOptions_can_be_set()
    {
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var options = new PqsClientOptions
        {
            ConnectionString = "Host=localhost",
            JsonSerializerOptions = jsonOptions
        };

        options.JsonSerializerOptions.Should().BeSameAs(jsonOptions);
    }

    [Fact]
    public void ConnectionString_is_required_via_data_annotations()
    {
        var options = new PqsClientOptions { ConnectionString = null! };
        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            options,
            new ValidationContext(options),
            results,
            validateAllProperties: true);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(PqsClientOptions.ConnectionString)));
    }

    [Fact]
    public void ConnectionString_rejects_empty_string_via_data_annotations()
    {
        var options = new PqsClientOptions { ConnectionString = "" };
        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            options,
            new ValidationContext(options),
            results,
            validateAllProperties: true);

        isValid.Should().BeFalse();
    }
}
