// Copyright 2026 Peaceful Studio OÜ

using Canton.Ledger.Auth.TokenGeneration;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Canton.Ledger.Auth.Tests;

public class ClientCredentialsOptionsValidatorTests
{
    private static ClientCredentialsOptions OptionsWithoutEndpoint => new()
    {
        ClientId = "client",
        ClientSecret = "secret"
    };

    [Fact]
    public void Validate_skips_instances_not_named_Options_DefaultName()
    {
        var validator = new ClientCredentialsOptionsValidator();

        var result = validator.Validate("other", OptionsWithoutEndpoint);

        result.Skipped.Should().BeTrue();
    }

    [Fact]
    public void Validate_fails_default_named_instance_without_Domain_or_TokenEndpoint()
    {
        var validator = new ClientCredentialsOptionsValidator();

        var result = validator.Validate(Options.DefaultName, OptionsWithoutEndpoint);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Domain or TokenEndpoint");
    }
}
