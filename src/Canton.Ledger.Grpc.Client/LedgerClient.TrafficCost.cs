// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Interactive = Com.Daml.Ledger.Api.V2.Interactive;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client;

public sealed partial class LedgerClient
{
    /// <inheritdoc />
    /// <remarks>
    /// Priced over the interactive submission service's <c>PrepareSubmission</c>. The per-call
    /// <paramref name="timeout"/> overrides <see cref="LedgerClientOptions.Timeout"/>; when both are
    /// <see langword="null"/> the call carries no deadline. The estimated total is tagged onto the
    /// call's activity as <c>canton.traffic_cost_bytes</c>.
    /// </remarks>
    /// <exception cref="RpcException">The participant rejected or failed the request.</exception>
    /// <exception cref="InvalidOperationException">
    /// A reported cost exceeded <see cref="long.MaxValue"/>. Unreachable for any real participant — that
    /// is over nine exabytes of traffic for one transaction — so it signals a corrupt or hostile
    /// response rather than an expensive submission; the message names the offending value.
    /// </exception>
    public Task<TrafficCostEstimate?> EstimateTrafficCostAsync(
        RuntimeCommands.CommandsSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var request = BuildPrepareSubmissionRequest(submission);

        return _invoker.ExecuteTracedAsync<LedgerClient, TrafficCostEstimate?>(
            LedgerCallInvoker.Source,
            Interactive.InteractiveSubmissionService.Descriptor,
            "PrepareSubmission",
            async (activity, token) =>
            {
                var response = await _invoker.InvokeAsync(
                    (headers, deadline, callToken) => _interactiveSubmissionService.PrepareSubmissionAsync(
                        request, headers, deadline, callToken),
                    token,
                    timeout).ConfigureAwait(false);

                var estimate = ProjectTrafficCostEstimate(response.CostEstimation);
                if (estimate is not null)
                {
                    activity?.SetTag(LedgerClientActivityTags.CantonTrafficCostBytes, estimate.TotalCost);
                }

                return estimate;
            },
            cancellationToken);
    }

    private static TrafficCostEstimate? ProjectTrafficCostEstimate(Interactive.CostEstimation? estimation) =>
        estimation is null
            ? null
            : new TrafficCostEstimate(
                estimation.EstimationTimestamp?.ToDateTimeOffset(),
                ToSignedCost(estimation.ConfirmationRequestTrafficCostEstimation, "confirmation request"),
                ToSignedCost(estimation.ConfirmationResponseTrafficCostEstimation, "confirmation response"),
                ToSignedCost(estimation.TotalTrafficCostEstimation, "total"));

    private static long ToSignedCost(ulong reportedCost, string component) =>
        reportedCost <= long.MaxValue
            ? (long)reportedCost
            : throw new InvalidOperationException(
                $"The participant reports a {component} traffic cost of {reportedCost} bytes, which exceeds the supported maximum of {long.MaxValue}.");

    private Interactive.PrepareSubmissionRequest BuildPrepareSubmissionRequest(
        RuntimeCommands.CommandsSubmission submission)
    {
        var commands = _commandBuilder.BuildCommands(submission);
        var request = new Interactive.PrepareSubmissionRequest
        {
            UserId = commands.UserId,
            CommandId = commands.CommandId,
            SynchronizerId = commands.SynchronizerId,
            EstimateTrafficCost = new Interactive.CostEstimationHints(),
        };

        request.ActAs.AddRange(commands.ActAs);
        request.ReadAs.AddRange(commands.ReadAs);
        request.Commands.AddRange(commands.Commands_);
        request.DisclosedContracts.AddRange(commands.DisclosedContracts);

        return request;
    }
}
