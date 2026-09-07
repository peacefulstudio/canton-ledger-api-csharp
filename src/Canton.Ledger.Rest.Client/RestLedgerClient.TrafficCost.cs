// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using RuntimeCommands = Daml.Runtime.Commands;
using WireCostEstimation = Canton.Ledger.Rest.Client.Raw.CostEstimation;
using WireCostEstimationHints = Canton.Ledger.Rest.Client.Raw.CostEstimationHints;
using WirePrepareSubmissionRequest = Canton.Ledger.Rest.Client.Raw.PrepareSubmissionRequest;
using WirePrepareSubmissionResponse = Canton.Ledger.Rest.Client.Raw.PrepareSubmissionResponse;

namespace Canton.Ledger.Rest.Client;

internal sealed partial class RestLedgerClient
{
    private const string PrepareSubmissionPath = "/v2/interactive-submission/prepare";
    private const string MissingPreparedSubmissionMessage =
        "Server returned a successful response but no prepared submission was present for the "
        + "traffic-cost estimate.";
    private const string MalformedTrafficCostBodyPrefix =
        "Server returned a malformed traffic-cost estimate response body: ";

    /// <inheritdoc />
    /// <remarks>
    /// Priced over <c>POST /v2/interactive-submission/prepare</c>. The per-call
    /// <paramref name="timeout"/> bounds the request; when <see langword="null"/> the call runs under
    /// the <see cref="HttpClient"/>'s own timeout. The cost does not reach a span, because this
    /// client's spans are emitted per HTTP request by the pipeline handler rather than per client
    /// method.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="submission"/> is <see langword="null"/>.</exception>
    /// <exception cref="LedgerOperationException">
    /// The participant rejected or failed the request, failed to answer it, or answered successfully
    /// with a body that is null or will not parse. The participant's category, error id and message
    /// are parsed off a rejection before it is thrown, as on every other call on this client — where
    /// the gRPC client throws <c>RpcException</c>. The neighbouring failures carry the same status
    /// codes every other timed call on this client reports: <c>408</c> for a
    /// <paramref name="timeout"/> overrun, naming the deadline, and <c>503</c> for a transport
    /// failure that never reached the participant. What none of them do is pass for an absent
    /// estimation — <see langword="null"/> is returned only when the participant answered and sent no
    /// estimation.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A reported cost was not a whole number of bytes, or exceeded <see cref="long.MaxValue"/>. The
    /// overflow is unreachable for any real participant — that is over nine exabytes of traffic for one
    /// transaction — so it signals a corrupt or hostile response rather than an expensive submission;
    /// the message names the offending value. This is the diagnosis for a cost served in the
    /// proto3-canonical string form; one served as a raw JSON number that overflows fails earlier, as a
    /// <see cref="JsonException"/> out of the deserializer. Either way an out-of-range cost is refused
    /// rather than wrapped to a negative one.
    /// </exception>
    public Task<TrafficCostEstimate?> EstimateTrafficCostAsync(
        RuntimeCommands.CommandsSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        return _calls.SendAsync<WirePrepareSubmissionResponse, TrafficCostEstimate?>(
            new RestCall(
                HttpMethod.Post, PrepareSubmissionPath, BuildPrepareSubmissionRequest(submission),
                MissingPreparedSubmissionMessage, MalformedTrafficCostBodyPrefix),
            body => ProjectTrafficCostEstimate(body.CostEstimation),
            timeout,
            cancellationToken);
    }

    private static TrafficCostEstimate? ProjectTrafficCostEstimate(WireCostEstimation? estimation) =>
        estimation is null
            ? null
            : new TrafficCostEstimate(
                estimation.EstimationTimestamp,
                ToSignedCost(estimation.ConfirmationRequestTrafficCostEstimation, "confirmation request"),
                ToSignedCost(estimation.ConfirmationResponseTrafficCostEstimation, "confirmation response"),
                ToSignedCost(estimation.TotalTrafficCostEstimation, "total"));

    private static long ToSignedCost(string? reportedCost, string component)
    {
        if (string.IsNullOrEmpty(reportedCost))
        {
            return 0L;
        }

        if (!ulong.TryParse(reportedCost, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidOperationException(
                $"The participant reports a {component} traffic cost of '{reportedCost}', which is not a whole number of bytes.");
        }

        return parsed <= long.MaxValue
            ? (long)parsed
            : throw new InvalidOperationException(
                $"The participant reports a {component} traffic cost of {parsed} bytes, which exceeds the supported maximum of {long.MaxValue}.");
    }

    private WirePrepareSubmissionRequest BuildPrepareSubmissionRequest(RuntimeCommands.CommandsSubmission submission)
    {
        var commands = RestCommandBuilder.BuildCommands(submission, _userId);
        var request = new WirePrepareSubmissionRequest
        {
            UserId = commands.UserId,
            CommandId = commands.CommandId,
            SynchronizerId = commands.SynchronizerId,
            ActAs = commands.ActAs,
            ReadAs = commands.ReadAs,
            Commands = commands.Commands1,
            EstimateTrafficCost = new WireCostEstimationHints(),
        };

        if (commands.DisclosedContracts is { Count: > 0 } disclosedContracts)
        {
            request.DisclosedContracts = disclosedContracts;
        }

        return request;
    }
}
