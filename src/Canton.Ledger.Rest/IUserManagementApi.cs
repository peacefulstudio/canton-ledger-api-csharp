// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Refit;

namespace Canton.Ledger.Rest.Client.Raw;

/// <summary>
/// Hand-authored overlay of <see cref="IUserManagementServiceApi"/>'s identity-provider query
/// parameter (off-spec tier). <c>GET /v2/users/{user-id}</c> reads it as <c>identity-provider-id</c>
/// rather than the proto-derived <c>identityProviderId</c>, and silently drops the caller's scope
/// under the generated name instead of rejecting it. <c>GET /v2/users</c>,
/// <c>DELETE /v2/users/{user-id}</c> and <c>GET /v2/users/{user-id}/rights</c> do not accept the
/// parameter under either spelling — their control case is <c>GetUser</c>, whose own
/// <c>400 Invalid value for: query parameter identity-provider-id</c> response shows the participant
/// would have named it here too if it validated it. Prefer this interface over
/// <see cref="IUserManagementServiceApi"/> for these four routes.
/// </summary>
[Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
public interface IUserManagementApi
{
    /// <summary>
    /// List all existing users. The participant does not accept an identity-provider scope on this
    /// route under any spelling, so none is offered here.
    /// </summary>
    /// <param name="pageToken">Pagination token for a subsequent page; omitted to fetch the first page.</param>
    /// <param name="pageSize">The maximum number of results the participant returns; it may return fewer.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the request.</param>
    /// <returns>The users known to the participant, one page at a time.</returns>
    /// <exception cref="ApiException">Thrown when the request returns a non-success status code.</exception>
    [Headers("Accept: application/json")]
    [Get("/v2/users")]
    Task<ListUsersResponse> ListUsers(
        [Query] string? pageToken = default,
        [Query] int? pageSize = default,
        CancellationToken cancellationToken = default);

    /// <summary>Get the user data of a specific user or the authenticated user.</summary>
    /// <param name="userId">
    /// The user whose data to retrieve. Empty string (the default) retrieves the authenticated user.
    /// </param>
    /// <param name="identityProviderId">
    /// The identity provider the user is managed by, sent as <c>identity-provider-id</c>. Omitted
    /// from the request when <see langword="null"/>, leaving the participant's default identity
    /// provider.
    /// </param>
    /// <param name="cancellationToken">The cancellation token to cancel the request.</param>
    /// <returns>The user data.</returns>
    /// <exception cref="ApiException">Thrown when the request returns a non-success status code.</exception>
    [Headers("Accept: application/json")]
    [Get("/v2/users/{userId}")]
    Task<GetUserResponse> GetUser(
        string userId,
        [Query][AliasAs("identity-provider-id")] string? identityProviderId = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete an existing user and all its rights. The participant does not accept an
    /// identity-provider scope on this route under any spelling, so none is offered here.
    /// </summary>
    /// <param name="userId">The user to delete.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the request.</param>
    /// <exception cref="ApiException">Thrown when the request returns a non-success status code.</exception>
    [Headers("Accept: application/json")]
    [Delete("/v2/users/{userId}")]
    Task<DeleteUserResponse> DeleteUser(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List the set of all rights granted to a user. The participant does not accept an
    /// identity-provider scope on this route under any spelling, so none is offered here.
    /// </summary>
    /// <param name="userId">
    /// The user for which to list the rights. Empty string (the default) lists the authenticated
    /// user's rights.
    /// </param>
    /// <param name="cancellationToken">The cancellation token to cancel the request.</param>
    /// <returns>The rights granted to the user.</returns>
    /// <exception cref="ApiException">Thrown when the request returns a non-success status code.</exception>
    [Headers("Accept: application/json")]
    [Get("/v2/users/{userId}/rights")]
    Task<ListUserRightsResponse> ListUserRights(
        string userId,
        CancellationToken cancellationToken = default);
}
