// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Canton.Ledger.Kernel.Authentication.TokenGeneration;

/// <summary>
/// Configuration for OAuth2 client-credentials token acquisition.
/// </summary>
public class ClientCredentialsOptions : IValidatableObject
{
    private const string InsecureTokenEndpointError =
        "The token endpoint uses plaintext http, which would expose the OAuth client secret to anyone "
        + "on the network path. Use an https endpoint, or set AllowInsecureTokenEndpoint to true to "
        + "accept the risk (e.g. against localhost during development).";

    /// <summary>
    /// OAuth2 client identifier.
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// OAuth2 client secret.
    /// </summary>
    public required string ClientSecret { get; set; }

    /// <summary>
    /// OAuth2 audience (e.g. <c>https://canton.network/</c>). Optional.
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>
    /// Identity-provider hostname (e.g. <c>dev-peaceful.eu.auth0.com</c>) or absolute
    /// http/https URL (e.g. <c>https://auth.example.com</c> or
    /// <c>https://auth.example.com/tenant-a</c>). A bare hostname is treated as
    /// <c>https://{hostname}</c>; an absolute URL is used as the base. In both cases
    /// <c>/oauth/token</c> is appended, preserving any existing path
    /// (<c>https://auth.example.com/tenant-a</c> →
    /// <c>https://auth.example.com/tenant-a/oauth/token</c>). Userinfo
    /// (<c>user:pass@host</c>), query strings, fragments, and values whose path already
    /// ends with <c>/oauth/token</c> are rejected — use <see cref="TokenEndpoint"/> for
    /// explicit token URIs. At least one of <see cref="Domain"/> or
    /// <see cref="TokenEndpoint"/> must be set; <see cref="TokenEndpoint"/> takes
    /// precedence when both are set.
    /// </summary>
    public string? Domain { get; set; }

    /// <summary>
    /// Explicit token endpoint URI. Takes precedence over <see cref="Domain"/>-derived endpoint.
    /// Use when the identity provider does not follow the <c>/oauth/token</c> convention
    /// (e.g. Keycloak: <c>/realms/{realm}/protocol/openid-connect/token</c>).
    /// Plaintext <c>http</c> endpoints are rejected unless
    /// <see cref="AllowInsecureTokenEndpoint"/> is set, because the token request sends the
    /// client secret in cleartext.
    /// </summary>
    public Uri? TokenEndpoint { get; set; }

    /// <summary>
    /// Opts in to sending the token request — including <see cref="ClientSecret"/> — to a
    /// plaintext <c>http</c> token endpoint, e.g. against localhost during development.
    /// Defaults to <see langword="false"/>, which rejects <c>http</c> endpoints at validation
    /// and construction time because anyone on the network path could read the secret.
    /// <see cref="ClientCredentialsProvider"/> logs a warning whenever a plaintext endpoint
    /// is actually used.
    /// </summary>
    public bool AllowInsecureTokenEndpoint { get; set; }

    /// <summary>
    /// How far before token expiry to trigger a refresh. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan SafetyMargin { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum time a single token-acquisition HTTP request may run before it is aborted.
    /// Bounds how long a hung identity provider can stall authenticated calls, since token
    /// fetches are serialized behind a single refresh lock. Defaults to 30 seconds. Must be
    /// positive. This governs only token acquisition; <c>LedgerClientOptions.Timeout</c>
    /// applies to the gRPC call.
    /// </summary>
    public TimeSpan TokenAcquisitionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Returns the effective token endpoint, preferring <see cref="TokenEndpoint"/>
    /// over the <see cref="Domain"/>-derived endpoint.
    /// </summary>
    public Uri TokenGenerationEndpoint
    {
        get
        {
            if (TokenEndpoint is not null)
            {
                if (!IsValidAbsoluteHttpUri(TokenEndpoint))
                    throw new InvalidOperationException("TokenEndpoint must be a valid absolute http/https URI.");
                return RequireOptInForPlaintextHttp(TokenEndpoint);
            }

            if (TryResolveDomainEndpoint(Domain, out var endpoint))
                return RequireOptInForPlaintextHttp(endpoint);

            if (DomainLooksLikeTokenEndpoint(Domain))
                throw new InvalidOperationException(
                    "Domain must not include the /oauth/token path — set TokenEndpoint instead for explicit token URIs.");

            throw new InvalidOperationException(
                "Either TokenEndpoint must be configured, or Domain must be a valid hostname or absolute http/https URI.");
        }
    }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            yield return new ValidationResult(
                "ClientId must not be whitespace.",
                [nameof(ClientId)]);
        }

        if (string.IsNullOrWhiteSpace(ClientSecret))
        {
            yield return new ValidationResult(
                "ClientSecret must not be whitespace.",
                [nameof(ClientSecret)]);
        }

        if (TokenEndpoint is null)
        {
            if (string.IsNullOrWhiteSpace(Domain))
            {
                yield return new ValidationResult(
                    "At least one of Domain or TokenEndpoint must be specified.",
                    [nameof(Domain), nameof(TokenEndpoint)]);
            }
            else if (DomainLooksLikeTokenEndpoint(Domain))
            {
                yield return new ValidationResult(
                    "Domain must not include the /oauth/token path — set TokenEndpoint instead for explicit token URIs.",
                    [nameof(Domain)]);
            }
            else if (!TryResolveDomainEndpoint(Domain, out var endpoint))
            {
                yield return new ValidationResult(
                    "Domain must be a valid hostname or absolute http/https URI when TokenEndpoint is not specified.",
                    [nameof(Domain)]);
            }
            else if (IsPlaintextHttpWithoutOptIn(endpoint))
            {
                yield return new ValidationResult(
                    InsecureTokenEndpointError,
                    [nameof(Domain), nameof(AllowInsecureTokenEndpoint)]);
            }
        }
        else if (!IsValidAbsoluteHttpUri(TokenEndpoint))
        {
            yield return new ValidationResult(
                "TokenEndpoint must be a valid absolute http/https URI.",
                [nameof(TokenEndpoint)]);
        }
        else if (IsPlaintextHttpWithoutOptIn(TokenEndpoint))
        {
            yield return new ValidationResult(
                InsecureTokenEndpointError,
                [nameof(TokenEndpoint), nameof(AllowInsecureTokenEndpoint)]);
        }

        if (SafetyMargin < TimeSpan.Zero)
        {
            yield return new ValidationResult(
                "SafetyMargin must not be negative.",
                [nameof(SafetyMargin)]);
        }

        if (TokenAcquisitionTimeout <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                "TokenAcquisitionTimeout must be positive.",
                [nameof(TokenAcquisitionTimeout)]);
        }
    }

    private static bool TryResolveDomainEndpoint(string? domain, [NotNullWhen(true)] out Uri? endpoint)
    {
        endpoint = null;
        if (!TryParseDomain(domain, out var uri))
            return false;

        var basePath = uri.AbsolutePath.TrimEnd('/');
        if (basePath.EndsWith("/oauth/token", StringComparison.OrdinalIgnoreCase))
            return false;

        endpoint = new Uri($"{uri.Scheme}://{uri.Authority}{basePath}/oauth/token");
        return true;
    }

    private static bool DomainLooksLikeTokenEndpoint(string? domain) =>
        TryParseDomain(domain, out var uri)
        && uri.AbsolutePath.TrimEnd('/').EndsWith("/oauth/token", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseDomain(string? domain, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(domain))
            return false;

        var trimmed = domain.Trim();
        var hasHttpScheme = trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        if (!hasHttpScheme && trimmed.Contains("://", StringComparison.Ordinal))
            return false;
        if (!hasHttpScheme && (trimmed.Contains('/') || trimmed.Contains('\\')))
            return false;

        var candidate = hasHttpScheme ? trimmed : $"https://{trimmed}";

        return Uri.TryCreate(candidate, UriKind.Absolute, out uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && Uri.CheckHostName(uri.Host) != UriHostNameType.Unknown
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }

    private static bool IsValidAbsoluteHttpUri(Uri uri) =>
        uri.IsAbsoluteUri
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private Uri RequireOptInForPlaintextHttp(Uri endpoint) =>
        IsPlaintextHttpWithoutOptIn(endpoint)
            ? throw new InvalidOperationException(InsecureTokenEndpointError)
            : endpoint;

    private bool IsPlaintextHttpWithoutOptIn(Uri endpoint) =>
        endpoint.Scheme == Uri.UriSchemeHttp && !AllowInsecureTokenEndpoint;
}
