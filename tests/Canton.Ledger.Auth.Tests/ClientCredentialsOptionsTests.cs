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

        results.Should().Contain(r =>
            r.MemberNames.Contains(nameof(ClientCredentialsOptions.Domain))
            && r.MemberNames.Contains(nameof(ClientCredentialsOptions.TokenEndpoint)));
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
    public void validation_fails_when_client_id_is_whitespace()
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = "   ",
            ClientSecret = "secret",
            Domain = "https://auth.example.com"
        };

        var results = Validate(options);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(ClientCredentialsOptions.ClientId)));
    }

    [Fact]
    public void validation_fails_when_client_secret_is_whitespace()
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = "id",
            ClientSecret = "   ",
            Domain = "https://auth.example.com"
        };

        var results = Validate(options);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(ClientCredentialsOptions.ClientSecret)));
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
            .WithMessage("*hostname*");
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

        results.Should().Contain(r =>
            r.MemberNames.Contains(nameof(ClientCredentialsOptions.Domain))
            && r.MemberNames.Contains(nameof(ClientCredentialsOptions.TokenEndpoint)));
    }

    [Fact]
    public void validation_fails_when_domain_has_invalid_hostname()
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            Domain = "not a hostname"
        };

        var results = Validate(options);

        results.Should().Contain(r => r.MemberNames.Contains("Domain"));
    }

    [Fact]
    public void token_generation_endpoint_derived_from_bare_hostname()
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            Domain = "dev-peaceful.eu.auth0.com"
        };

        options.TokenGenerationEndpoint
            .Should().Be(new Uri("https://dev-peaceful.eu.auth0.com/oauth/token"));
    }

    [Fact]
    public void bare_hostname_passes_validation()
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            Domain = "dev-peaceful.eu.auth0.com"
        };

        var results = Validate(options);

        results.Should().NotContain(r => r.MemberNames.Contains("Domain"));
    }

    [Theory]
    [InlineData("https://auth.example.com", "https://auth.example.com/oauth/token")]
    [InlineData("https://auth.example.com/", "https://auth.example.com/oauth/token")]
    [InlineData("http://auth.example.com", "http://auth.example.com/oauth/token")]
    [InlineData("HTTPS://auth.example.com", "https://auth.example.com/oauth/token")]
    [InlineData("auth.example.com:8443", "https://auth.example.com:8443/oauth/token")]
    [InlineData("https://auth.example.com:8443", "https://auth.example.com:8443/oauth/token")]
    [InlineData("https://auth.example.com/tenant-a", "https://auth.example.com/tenant-a/oauth/token")]
    [InlineData("https://auth.example.com/tenant-a/", "https://auth.example.com/tenant-a/oauth/token")]
    [InlineData("  dev-peaceful.eu.auth0.com  ", "https://dev-peaceful.eu.auth0.com/oauth/token")]
    public void token_generation_endpoint_composes_from_domain(string domain, string expected)
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            Domain = domain
        };

        options.TokenGenerationEndpoint.Should().Be(new Uri(expected));
    }

    [Theory]
    [InlineData("not a hostname")]
    [InlineData("   ")]
    [InlineData("https://")]
    [InlineData("ftp://auth.example.com")]
    [InlineData("https://user:pass@auth.example.com")]
    [InlineData("https://auth.example.com?foo=bar")]
    [InlineData("https://auth.example.com#frag")]
    [InlineData("https://auth.example.com/oauth/token")]
    [InlineData("https://auth.example.com/oauth/token/")]
    [InlineData("auth.example.com/oauth/token")]
    [InlineData("auth.example.com/tenant-a")]
    [InlineData("auth.example.com\\tenant-a")]
    public void invalid_domain_values_fail_validation(string domain)
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            Domain = domain
        };

        var results = Validate(options);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(ClientCredentialsOptions.Domain)));
    }

    [Fact]
    public void domain_ending_in_oauth_token_suggests_token_endpoint()
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            Domain = "https://auth.example.com/oauth/token"
        };

        var results = Validate(options);

        results.Should().Contain(r =>
            r.MemberNames.Contains(nameof(ClientCredentialsOptions.Domain))
            && r.ErrorMessage!.Contains(nameof(ClientCredentialsOptions.TokenEndpoint)));
    }

    [Fact]
    public void token_generation_endpoint_throws_actionable_message_for_oauth_token_suffix()
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            Domain = "https://auth.example.com/oauth/token"
        };

        var act = () => options.TokenGenerationEndpoint;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*/oauth/token*TokenEndpoint*");
    }

    [Fact]
    public void validation_passes_when_only_token_endpoint_set()
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            TokenEndpoint = new Uri("https://idp.example.com/custom/token")
        };

        var results = Validate(options);

        results.Should().NotContain(r =>
            r.MemberNames.Contains(nameof(ClientCredentialsOptions.Domain))
            || r.MemberNames.Contains(nameof(ClientCredentialsOptions.TokenEndpoint)));
    }

    [Fact]
    public void token_generation_endpoint_returns_token_endpoint_when_domain_absent()
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            TokenEndpoint = new Uri("https://idp.example.com/custom/token")
        };

        options.TokenGenerationEndpoint.Should().Be(new Uri("https://idp.example.com/custom/token"));
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
