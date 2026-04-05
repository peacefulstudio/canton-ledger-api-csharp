// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using System.ComponentModel.DataAnnotations;
using Canton.Ledger.Auth.TokenGeneration;
using FluentAssertions;
using Xunit;

namespace Canton.Ledger.Auth.Tests;

public class ClientCredentialsOptionsTests
{
    [Fact]
    public void default_safety_margin_is_30_seconds()
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            Domain = "https://auth.example.com"
        };

        options.SafetyMargin.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void token_generation_endpoint_derived_from_domain()
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            Domain = "https://auth.example.com"
        };

        options.TokenGenerationEndpoint.Should().Be(new Uri("https://auth.example.com/oauth/token"));
    }

    [Fact]
    public void explicit_token_endpoint_takes_precedence()
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            Domain = "https://auth.example.com",
            TokenEndpoint = new Uri("https://custom.example.com/token")
        };

        options.TokenGenerationEndpoint.Should().Be(new Uri("https://custom.example.com/token"));
    }

    [Fact]
    public void validation_fails_when_no_domain_and_no_token_endpoint()
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = "id",
            ClientSecret = "secret"
        };

        var results = Validate(options);

        results.Should().Contain(r => r.ErrorMessage!.Contains("Domain") || r.ErrorMessage!.Contains("TokenEndpoint"));
    }

    [Fact]
    public void validation_fails_when_client_id_missing()
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = null!,
            ClientSecret = "secret",
            Domain = "https://auth.example.com"
        };

        var results = Validate(options);

        results.Should().Contain(r => r.MemberNames.Contains("ClientId"));
    }

    [Fact]
    public void validation_fails_when_client_secret_missing()
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = "id",
            ClientSecret = null!,
            Domain = "https://auth.example.com"
        };

        var results = Validate(options);

        results.Should().Contain(r => r.MemberNames.Contains("ClientSecret"));
    }

    [Fact]
    public void validation_fails_when_safety_margin_is_negative()
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            Domain = "https://auth.example.com",
            SafetyMargin = TimeSpan.FromSeconds(-1)
        };

        var results = Validate(options);

        results.Should().Contain(r => r.MemberNames.Contains("SafetyMargin"));
    }

    [Fact]
    public void token_generation_endpoint_throws_when_neither_domain_nor_endpoint_set()
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = "id",
            ClientSecret = "secret"
        };

        var act = () => options.TokenGenerationEndpoint;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TokenEndpoint*Domain*");
    }

    [Fact]
    public void validation_fails_when_domain_is_empty()
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            Domain = ""
        };

        var results = Validate(options);

        results.Should().Contain(r => r.MemberNames.Contains("Domain") || r.MemberNames.Contains("TokenEndpoint"));
    }

    [Fact]
    public void validation_fails_when_domain_is_not_valid_uri()
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            Domain = "not-a-uri"
        };

        var results = Validate(options);

        results.Should().Contain(r => r.MemberNames.Contains("Domain"));
    }

    [Fact]
    public void validation_fails_when_token_endpoint_is_relative()
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            TokenEndpoint = new Uri("/oauth/token", UriKind.Relative)
        };

        var results = Validate(options);

        results.Should().Contain(r => r.MemberNames.Contains("TokenEndpoint"));
    }

    private static List<ValidationResult> Validate(ClientCredentialsOptions options)
    {
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, context, results, validateAllProperties: true);
        return results;
    }
}
