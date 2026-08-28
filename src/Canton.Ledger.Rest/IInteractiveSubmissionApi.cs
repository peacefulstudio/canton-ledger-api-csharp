// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Refit;

namespace Canton.Ledger.Rest.Client.Raw;

/// <summary>
/// Hand-authored client for <c>GET /v2/interactive-submission/preferred-package-version</c>
/// (off-spec tier). The participant reads three of that route's query parameters under names the
/// annotated protos do not derive, which leaves the proto-derived
/// <see cref="IInteractiveSubmissionServiceApi.GetPreferredPackageVersion"/> uncallable rather than
/// merely fragile: it sends the required package name as <c>packageName</c>, and the participant
/// answers <c>400 Invalid value for: query parameter package-name (missing)</c> — to every call,
/// unconditionally. The two optional parameters fail the opposite and more dangerous way. Sent as
/// <c>synchronizerId</c> and <c>vettingValidAt</c> they are discarded in silence, so repairing only
/// the required name would answer <c>200</c> with neither the synchronizer scoping nor the
/// vetting-time bound the caller asked for. The served names are <c>package-name</c>,
/// <c>synchronizer-id</c> and <c>vetting_valid_at</c> — plain, kebab and snake on one route, with
/// only <c>parties</c> agreeing, so no single rename rule derives them. Prefer this interface for
/// reading a preferred package version.
/// </summary>
/// <remarks>
/// The participant deprecates this route for removal in Canton 3.6 and directs callers to
/// <see cref="IInteractiveSubmissionServiceApi.GetPreferredPackages"/>. That successor takes its
/// vetting requirements as a request body rather than as query parameters and answers with a
/// package preference per package name, so moving to it is a change of call shape, not a mechanical
/// substitution of one member for another.
/// </remarks>
[Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
public interface IInteractiveSubmissionApi
{
    /// <summary>
    /// Gets the preferred package version for constructing a command submission: the
    /// highest-versioned package for the given package name that every participant hosting the given
    /// parties has vetted.
    /// </summary>
    /// <param name="parties">The parties whose hosting participants must have vetted the package; each value is sent as its own <c>parties</c> occurrence.</param>
    /// <param name="packageName">The package name to resolve a preferred version for, sent as <c>package-name</c>. The participant requires it and rejects a request without it.</param>
    /// <param name="synchronizerId">The synchronizer whose topology state the vetting is resolved against, sent as <c>synchronizer-id</c>. Omitted from the request when null, leaving the preference resolved against the vetting states of every synchronizer the participant is connected to. Under the proto-derived spelling it is discarded without a word, so the preference comes back unscoped whatever the caller asked for.</param>
    /// <param name="vettingValidAt">The timestamp to compute vetting validity at, sent as <c>vetting_valid_at</c> in the round-trip ISO-8601 form the route parses; the participant rejects .NET's default <c>MM/dd/yyyy HH:mm:ss zzz</c> rendering. Omitted from the request when null, leaving the participant's current clock time; under the proto-derived spelling it is discarded without a word.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the request.</param>
    /// <returns>The preferred package, whose package preference is unpopulated when none satisfies the requirements.</returns>
    /// <exception cref="ApiException">Thrown when the request returns a non-success status code.</exception>
    [Headers("Accept: application/json")]
    [Get("/v2/interactive-submission/preferred-package-version")]
    Task<GetPreferredPackageVersionResponse> GetPreferredPackageVersion(
        [Query(CollectionFormat.Multi)] IEnumerable<string> parties,
        [Query][AliasAs("package-name")] string packageName,
        [Query][AliasAs("synchronizer-id")] string? synchronizerId,
        [Query(Format = "O")][AliasAs("vetting_valid_at")] DateTimeOffset? vettingValidAt,
        CancellationToken cancellationToken = default);
}
