// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Refit;

namespace Canton.Ledger.Rest.Client.Raw;

/// <summary>
/// Hand-authored client for reading a package archive back from the participant (off-spec tier).
/// The JSON Ledger API serves the archive as a raw <c>application/octet-stream</c> body and returns
/// its hash in the <see cref="PackageHashHeader"/> response header, a shape the annotated protos
/// cannot derive: the proto-derived <see cref="IPackageServiceApi.GetPackage"/> instead asks for a
/// JSON envelope carrying the archive as a base64 field beside the hash. Prefer this interface for
/// reading package archives.
/// </summary>
[Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
public interface IPackageApi
{
    /// <summary>The response header the participant returns the package archive's hash in.</summary>
    public const string PackageHashHeader = "Canton-Package-Hash";

    /// <summary>Downloads a package archive from the participant node.</summary>
    /// <param name="packageId">The id of the package to download.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the request.</param>
    /// <returns>
    /// The response, whose content is the raw archive bytes and whose
    /// <see cref="IApiResponse.Headers"/> carry the archive hash under <see cref="PackageHashHeader"/>.
    /// A non-success status is a value on the response, never an exception; dispose the response to
    /// release the archive stream.
    /// </returns>
    [Headers("Accept: application/octet-stream")]
    [Get("/v2/packages/{packageId}")]
    Task<IApiResponse<Stream>> GetPackage(string packageId, CancellationToken cancellationToken = default);
}
