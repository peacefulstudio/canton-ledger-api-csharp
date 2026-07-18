// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Refit;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Hand-authored client for <c>GET /v2/authenticated-user</c>, the JSON-only route the
/// annotated ledger protos cannot derive (off-spec tier, ADR 0005 / #170). Returns the
/// same <see cref="GetUserResponse"/> shape as the generated user-management endpoints.
/// </summary>
public interface IAuthenticatedUserApi
{
    /// <summary>Gets the user data of the currently authenticated user.</summary>
    /// <param name="identityProviderId">Optional identity provider to resolve the user against; omitted from the request when null.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the request.</param>
    /// <returns>The authenticated user.</returns>
    /// <exception cref="ApiException">Thrown when the request returns a non-success status code.</exception>
    [Headers("Accept: application/json")]
    [Get("/v2/authenticated-user")]
    Task<GetUserResponse> GetAuthenticatedUser(
        [Query][AliasAs("identity-provider-id")] string? identityProviderId = null,
        CancellationToken cancellationToken = default);
}
