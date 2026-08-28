// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Refit;

namespace Canton.Ledger.Rest.Client.Raw;

/// <summary>
/// Hand-authored client for the participant's DAR routes (off-spec tier). The JSON Ledger API takes
/// the DAR as a raw <c>application/octet-stream</c> body with the vetting and synchronizer choices as
/// query parameters, a shape the annotated protos cannot derive: the proto-derived
/// <see cref="IPackageManagementServiceApi.UploadDarFile"/> and
/// <see cref="IPackageManagementServiceApi.ValidateDarFile"/> instead post a base64 JSON envelope the
/// participant rejects. Prefer this interface for DAR upload and validation.
/// </summary>
[Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
public interface IDarApi
{
    /// <summary>Uploads a DAR to the participant node.</summary>
    /// <param name="darFile">The raw DAR bytes, streamed unencoded as the request body.</param>
    /// <param name="vetAllPackages">Whether to vet every package in the DAR; omitted from the request when null, leaving the participant's default.</param>
    /// <param name="synchronizerId">The synchronizer to vet the packages on; omitted from the request when null.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the request.</param>
    /// <exception cref="ApiException">Thrown when the request returns a non-success status code.</exception>
    [Headers("Accept: application/json", "Content-Type: application/octet-stream")]
    [Post("/v2/dars")]
    Task UploadDar(
        [Body] Stream darFile,
        [Query] bool? vetAllPackages = null,
        [Query] string? synchronizerId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a DAR and checks its packages for upgrade compatibility against the packages already
    /// vetted on the target synchronizer, without persisting the DAR or vetting anything.
    /// </summary>
    /// <param name="darFile">The raw DAR bytes, streamed unencoded as the request body.</param>
    /// <param name="synchronizerId">The synchronizer to check compatibility against; omitted from the request when null.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the request.</param>
    /// <exception cref="ApiException">Thrown when the request returns a non-success status code.</exception>
    [Headers("Accept: application/json", "Content-Type: application/octet-stream")]
    [Post("/v2/dars/validate")]
    Task ValidateDar(
        [Body] Stream darFile,
        [Query] string? synchronizerId = null,
        CancellationToken cancellationToken = default);
}
