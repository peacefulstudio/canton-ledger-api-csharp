// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Peaceful.Canton.Localnet.Testing;

namespace Canton.Ledger.Testing.Localnet;

/// <summary>
/// Grants a LocalNet ledger user the <c>CanActAs</c> rights one test lane needs, and revokes them
/// again when the lane disposes.
/// </summary>
/// <remarks>
/// A participant caps a user at 1000 rights by default and a party is never deletable, so a suite
/// that takes a right per run and never gives it back saturates the participant every contributor
/// and every CI run shares — every later grant on it then fails with <c>TOO_MANY_USER_RIGHTS</c>.
/// The revoke runs on <see cref="CancellationToken.None"/> so that a run cancelled mid-test still
/// hands its rights back, and it verifies the participant's own account of what it revoked so that
/// a revoke matching nothing fails the run instead of passing as a silent no-op.
/// </remarks>
public sealed class ActAsRightsLease : IAsyncDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly Uri _rightsEndpoint;
    private readonly string _userId;
    private readonly Func<CancellationToken, ValueTask<string>> _getAccessToken;
    private readonly List<string> _leasedParties = [];

    internal ActAsRightsLease(
        Uri jsonLedgerApi,
        string userId,
        Func<CancellationToken, ValueTask<string>> getAccessToken,
        HttpMessageHandler? handler = null)
    {
        _userId = userId;
        _getAccessToken = getAccessToken;
        _rightsEndpoint = new Uri(
            EnsureTrailingSlash(jsonLedgerApi), $"v2/users/{Uri.EscapeDataString(userId)}/rights");
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _httpClient.Timeout = RequestTimeout;
    }

    /// <summary>Opens a lease over the rights of the fixture's own validator user.</summary>
    public static ActAsRightsLease ForValidator(LocalnetFixture fixture) => new(
        fixture.Endpoints.JsonLedgerApi,
        fixture.ValidatorUserId,
        fixture.TokenProvider.GetAccessTokenAsync);

    /// <summary>
    /// Grants the user <c>CanActAs</c> on <paramref name="partyId"/> and holds that right until
    /// this lease is disposed.
    /// </summary>
    /// <exception cref="InvalidOperationException">The participant rejected the grant.</exception>
    public async Task GrantAsync(string partyId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Post, [partyId], cancellationToken).ConfigureAwait(false);
        _leasedParties.Add(partyId);
    }

    /// <summary>Revokes every right this lease granted, in one request.</summary>
    /// <exception cref="InvalidOperationException">
    /// The participant rejected the revoke, or reported that it left one of the rights standing.
    /// </exception>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_leasedParties.Count == 0)
            {
                return;
            }

            var revoking = _leasedParties.ToArray();
            _leasedParties.Clear();
            var response = await SendAsync(HttpMethod.Patch, revoking, CancellationToken.None)
                .ConfigureAwait(false);
            AssertNothingStillHeld(revoking, response);
        }
        finally
        {
            _httpClient.Dispose();
        }
    }

    private void AssertNothingStillHeld(IReadOnlyList<string> revoking, string response)
    {
        var stillHeld = revoking.Except(RevokedParties(response), StringComparer.Ordinal).ToArray();
        if (stillHeld.Length == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"PATCH {_rightsEndpoint} succeeded but revoked "
            + $"{revoking.Count - stillHeld.Length} of {revoking.Count} act-as right(s); the user "
            + $"still holds {string.Join(", ", stillHeld)}. Response: {response}");
    }

    private static IEnumerable<string> RevokedParties(string response)
    {
        using var document = JsonDocument.Parse(response);
        if (!document.RootElement.TryGetProperty("newlyRevokedRights", out var rights)
            || rights.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return rights.EnumerateArray()
            .Select(right => right.TryGetProperty("kind", out var kind)
                && kind.TryGetProperty("CanActAs", out var canActAs)
                && canActAs.TryGetProperty("value", out var value)
                && value.TryGetProperty("party", out var party)
                    ? party.GetString()
                    : null)
            .OfType<string>()
            .ToArray();
    }

    private async Task<string> SendAsync(
        HttpMethod method, IReadOnlyList<string> parties, CancellationToken cancellationToken)
    {
        var token = await _getAccessToken(cancellationToken).ConfigureAwait(false);
        var rights = parties
            .Select(party => new UserRight(new UserRightKind(new UserRightParty(new UserRightPartyValue(party)))))
            .ToArray();

        using var request = new HttpRequestMessage(method, _rightsEndpoint)
        {
            Content = JsonContent.Create(new UserRightsRequest(_userId, string.Empty, rights)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"{method} {_rightsEndpoint} for {string.Join(", ", parties)} returned "
                + $"{(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        return body;
    }

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");

    private sealed record UserRightsRequest(
        [property: JsonPropertyName("userId")] string UserId,
        [property: JsonPropertyName("identityProviderId")] string IdentityProviderId,
        [property: JsonPropertyName("rights")] IReadOnlyList<UserRight> Rights);

    private sealed record UserRight(
        [property: JsonPropertyName("kind")] UserRightKind Kind);

    private sealed record UserRightKind(
        [property: JsonPropertyName("CanActAs")] UserRightParty CanActAs);

    private sealed record UserRightParty(
        [property: JsonPropertyName("value")] UserRightPartyValue Value);

    private sealed record UserRightPartyValue(
        [property: JsonPropertyName("party")] string Party);
}
