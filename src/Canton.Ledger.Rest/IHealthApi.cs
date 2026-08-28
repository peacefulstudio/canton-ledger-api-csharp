// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Refit;

namespace Canton.Ledger.Rest.Client.Raw;

/// <summary>
/// Hand-authored client for the JSON Ledger API health probes, which proxy gRPC health and
/// therefore sit outside the proto-derived OpenAPI spec (off-spec tier).
/// Probe outcomes are values: a not-ready participant answers 503 on <c>/readyz</c>, and that
/// status is returned as an <see cref="IApiResponse"/> rather than thrown as an exception.
/// </summary>
[Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
public interface IHealthApi
{
    /// <summary>Checks whether the participant's JSON API service is alive.</summary>
    /// <param name="cancellationToken">The cancellation token to cancel the request.</param>
    /// <returns>The probe response; inspect <see cref="IApiResponse.IsSuccessStatusCode"/>.</returns>
    [Get("/livez")]
    Task<IApiResponse> CheckLiveness(CancellationToken cancellationToken = default);

    /// <summary>Checks whether the participant's JSON API service is ready to serve requests.</summary>
    /// <param name="cancellationToken">The cancellation token to cancel the request.</param>
    /// <returns>The probe response; 503 not-ready is a value, never an exception.</returns>
    [Get("/readyz")]
    Task<IApiResponse> CheckReadiness(CancellationToken cancellationToken = default);
}
