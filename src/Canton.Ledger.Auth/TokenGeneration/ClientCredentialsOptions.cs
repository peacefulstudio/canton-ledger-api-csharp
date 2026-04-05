// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using System.ComponentModel.DataAnnotations;

namespace Canton.Ledger.Auth.TokenGeneration;

/// <summary>
/// Configuration for OAuth2 client-credentials token acquisition.
/// </summary>
public class ClientCredentialsOptions : IValidatableObject
{
    /// <summary>
    /// OAuth2 client identifier.
    /// </summary>
    [Required]
    public required string ClientId { get; set; }

    /// <summary>
    /// OAuth2 client secret.
    /// </summary>
    [Required]
    public required string ClientSecret { get; set; }

    /// <summary>
    /// OAuth2 audience (e.g. <c>https://canton.network/</c>). Optional.
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>
    /// Base domain of the identity provider (e.g. <c>https://auth.example.com</c>).
    /// Used to derive the token endpoint as <c>{Domain}/oauth/token</c>.
    /// At least one of <see cref="Domain"/> or <see cref="TokenEndpoint"/> must be set.
    /// </summary>
    public string? Domain { get; set; }

    /// <summary>
    /// Explicit token endpoint URI. Takes precedence over <see cref="Domain"/>-derived endpoint.
    /// </summary>
    public Uri? TokenEndpoint { get; set; }

    /// <summary>
    /// How far before token expiry to trigger a refresh. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan SafetyMargin { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Returns the effective token endpoint, preferring <see cref="TokenEndpoint"/>
    /// over the <see cref="Domain"/>-derived endpoint.
    /// </summary>
    public Uri TokenGenerationEndpoint =>
        TokenEndpoint is not null
            ? (TokenEndpoint.IsAbsoluteUri && (TokenEndpoint.Scheme == Uri.UriSchemeHttp || TokenEndpoint.Scheme == Uri.UriSchemeHttps)
                ? TokenEndpoint
                : throw new InvalidOperationException("TokenEndpoint must be a valid absolute http/https URI."))
            : IsValidHttpUri(Domain)
                ? new Uri($"{Domain!.TrimEnd('/')}/oauth/token")
                : throw new InvalidOperationException(
                    "Either TokenEndpoint must be configured, or Domain must be a valid absolute http/https URI.");

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TokenEndpoint is null)
        {
            if (string.IsNullOrWhiteSpace(Domain))
            {
                yield return new ValidationResult(
                    "At least one of Domain or TokenEndpoint must be specified.",
                    [nameof(Domain), nameof(TokenEndpoint)]);
            }
            else if (!IsValidHttpUri(Domain))
            {
                yield return new ValidationResult(
                    "Domain must be a valid absolute http/https URI when TokenEndpoint is not specified.",
                    [nameof(Domain)]);
            }
        }
        else if (!TokenEndpoint.IsAbsoluteUri
            || (TokenEndpoint.Scheme != Uri.UriSchemeHttp && TokenEndpoint.Scheme != Uri.UriSchemeHttps))
        {
            yield return new ValidationResult(
                "TokenEndpoint must be a valid absolute http/https URI.",
                [nameof(TokenEndpoint)]);
        }

        if (SafetyMargin < TimeSpan.Zero)
        {
            yield return new ValidationResult(
                "SafetyMargin must not be negative.",
                [nameof(SafetyMargin)]);
        }
    }

    private static bool IsValidHttpUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
