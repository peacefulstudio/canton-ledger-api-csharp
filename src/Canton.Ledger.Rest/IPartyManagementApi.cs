// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Refit;

namespace Canton.Ledger.Rest.Client.Raw;

/// <summary>
/// Hand-authored overlay of <see cref="IPartyManagementServiceApi"/>'s party-filter and
/// identity-provider query parameters (off-spec tier). The proto-derived interface sends them as
/// <c>filterParty</c> and <c>identityProviderId</c>; the participant reads <c>filter-party</c> and
/// <c>identity-provider-id</c> instead and ignores the unrecognized camelCase names without an
/// error, so a caller of the generated interface has its party filter or identity-provider scope
/// silently dropped rather than rejected. Prefer this interface for
/// <c>GET /v2/parties</c> and <c>GET /v2/parties/{party}</c>.
/// </summary>
[Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
public interface IPartyManagementApi
{
    /// <summary>List the parties known by the participant.</summary>
    /// <param name="pageToken">Pagination token for a subsequent page; omitted to fetch the first page.</param>
    /// <param name="pageSize">The maximum number of results the participant returns; it may return fewer.</param>
    /// <param name="identityProviderId">
    /// The identity provider whose parties should be retrieved, sent as
    /// <c>identity-provider-id</c>. Omitted from the request when <see langword="null"/>, leaving
    /// the participant's default identity provider.
    /// </param>
    /// <param name="filterParty">
    /// An optional prefix filter on the party name, sent as <c>filter-party</c>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token to cancel the request.</param>
    /// <returns>The parties known to the participant, one page at a time.</returns>
    /// <exception cref="ApiException">Thrown when the request returns a non-success status code.</exception>
    [Headers("Accept: application/json")]
    [Get("/v2/parties")]
    Task<ListKnownPartiesResponse> ListKnownParties(
        [Query] string? pageToken = default,
        [Query] int? pageSize = default,
        [Query][AliasAs("identity-provider-id")] string? identityProviderId = default,
        [Query][AliasAs("filter-party")] string? filterParty = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the party details of the given party. The party is returned only when the participant
    /// knows it, so an unknown party answers with an empty list rather than an error.
    /// </summary>
    /// <param name="party">The stable, unique identifier of the Daml party.</param>
    /// <param name="identityProviderId">
    /// The identity provider whose parties should be retrieved, sent as
    /// <c>identity-provider-id</c>. Omitted from the request when <see langword="null"/>, leaving
    /// the participant's default identity provider.
    /// </param>
    /// <param name="cancellationToken">The cancellation token to cancel the request.</param>
    /// <returns>The details of the party, or an empty list when the participant does not know it.</returns>
    /// <exception cref="ApiException">Thrown when the request returns a non-success status code.</exception>
    [Headers("Accept: application/json")]
    [Get("/v2/parties/{party}")]
    Task<GetPartiesResponse> GetParties(
        string party,
        [Query][AliasAs("identity-provider-id")] string? identityProviderId = default,
        CancellationToken cancellationToken = default);
}
