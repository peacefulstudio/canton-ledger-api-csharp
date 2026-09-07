// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace Canton.Ledger.Rest.Client.Raw;

/// <summary>
/// The multi-party read the JSON Ledger API does not serve, over the single-party route it does.
/// </summary>
[Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
public static class PartyManagementServiceApiExtensions
{
    /// <summary>
    /// Reads the details of several parties, the read the gRPC Ledger API's
    /// <c>PartyManagementService.GetParties</c> serves in one call from a repeated request field.
    /// </summary>
    /// <param name="api">The party management interface to read through.</param>
    /// <param name="parties">
    /// The parties to read, in the order their details come back. An empty sequence issues no
    /// request and answers with an empty list.
    /// </param>
    /// <param name="identityProviderId">
    /// The identity provider whose parties are read; omitted from every request when
    /// <see langword="null"/>, leaving the participant's default identity provider.
    /// </param>
    /// <param name="cancellationToken">The cancellation token to cancel the read.</param>
    /// <returns>
    /// The details of every party the participant knows, in the order asked for. A party the
    /// participant does not know contributes nothing, because
    /// <see cref="IPartyManagementServiceApi.GetParties"/> answers an unknown party with an empty
    /// list rather than an error — so a read of N parties can come back with fewer than N details,
    /// exactly as the gRPC batch does.
    /// </returns>
    /// <remarks>
    /// <b>This read costs one round trip per party, where the gRPC transport costs one in total.</b>
    /// <c>GET /v2/parties/{party}</c> carries a single party in its path segment: the participant
    /// reads a comma-delimited segment as one party identifier and refuses it with
    /// <c>non expected character 0x2c in Daml-LF Party</c>. The requests are issued one after
    /// another, so a read of many parties is as slow as the participant is, and the traffic cost of
    /// the same read differs by transport.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown synchronously when <paramref name="api"/> or <paramref name="parties"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="Refit.ApiException">
    /// Thrown when any of the requests returns a non-success status code. The reads before it have
    /// already reached the participant.
    /// </exception>
    public static Task<IReadOnlyList<PartyDetails>> GetPartiesAsync(
        this IPartyManagementServiceApi api,
        IEnumerable<string> parties,
        string? identityProviderId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(parties);

        return ReadPartyByPartyAsync(api, parties, identityProviderId, cancellationToken);
    }

    private static async Task<IReadOnlyList<PartyDetails>> ReadPartyByPartyAsync(
        IPartyManagementServiceApi api,
        IEnumerable<string> parties,
        string? identityProviderId,
        CancellationToken cancellationToken)
    {
        List<PartyDetails> details = [];

        foreach (var party in parties)
        {
            var response = await api
                .GetParties(party, identityProviderId, cancellationToken)
                .ConfigureAwait(false);

            details.AddRange(response.PartyDetails);
        }

        return details;
    }
}
