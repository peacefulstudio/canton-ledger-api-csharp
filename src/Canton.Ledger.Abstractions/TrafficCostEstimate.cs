// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Abstractions;

/// <summary>
/// What a participant estimates a prepared transaction would consume in synchronizer traffic,
/// in bytes, measured against the synchronizer chosen while preparing it. Both transports project
/// the participant's estimate into this one shape.
/// </summary>
/// <param name="EstimatedAt">
/// When the participant computed the estimate, or <see langword="null"/> when it sent no timestamp.
/// Canton prices traffic against synchronizer state that moves, so an estimate is only as good as its age.
/// </param>
/// <param name="ConfirmationRequestCost">
/// Estimated traffic cost of the confirmation request the transaction would produce.
/// </param>
/// <param name="ConfirmationResponseCost">
/// Estimated traffic cost of the confirmation response the transaction would produce. Canton documents
/// this as also indicating what each other node confirming for the party spends approving or rejecting it.
/// </param>
/// <param name="TotalCost">
/// The total the participant reports, which Canton documents as the sum of
/// <paramref name="ConfirmationRequestCost"/> and <paramref name="ConfirmationResponseCost"/>. It is read
/// from the response rather than added up here, so the participant remains the authority on its own total.
/// </param>
/// <remarks>
/// Two costs are outside the estimate: reassigning contracts to another synchronizer when that is
/// necessary, and request amplification — each amplified request additionally costs one confirmation
/// request.
/// </remarks>
public sealed record TrafficCostEstimate(
    DateTimeOffset? EstimatedAt,
    long ConfirmationRequestCost,
    long ConfirmationResponseCost,
    long TotalCost);
