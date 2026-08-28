// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Streams;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using RuntimeCommands = Daml.Runtime.Commands;
using WireCompletionResponse = Canton.Ledger.Rest.Client.Raw.CompletionResponse;
using WireCompletionStreamResponse = Canton.Ledger.Rest.Client.Raw.CompletionStreamResponse;

namespace Canton.Ledger.Rest.Client;

public sealed partial class RestLedgerClient
{
    private const string AsyncSubmitPath = "/v2/commands/async/submit";
    private const string AsyncSubmitReassignmentPath = "/v2/commands/async/submit-reassignment";
    private const string SubmitAndWaitForReassignmentPath = "/v2/commands/submit-and-wait-for-reassignment";
    private const string ConnectedSynchronizersPath = "/v2/state/connected-synchronizers";
    private const string LedgerApiVersionPath = "/v2/version";
    private const string UpdateByOffsetPath = "/v2/updates/update-by-offset";
    private const string UpdateByIdPath = "/v2/updates/update-by-id";
    private const string CompletionsPath = "/v2/commands/completions";
    private const int NoTransportFailureStatusCode = 0;
    private const string LimitQueryParameter = "limit";
    private const string StreamIdleTimeoutQueryParameter = "stream_idle_timeout_ms";

    /// <inheritdoc />
    /// <remarks>
    /// Fire-and-forget over one blocking <c>POST /v2/commands/async/submit</c> call. A transport
    /// failure or non-success response throws <see cref="LedgerOperationException"/>; the verdict is
    /// observed on the completion stream, not awaited here.
    /// </remarks>
    public async Task<RuntimeCommands.CommandId> SubmitAsync(
        RuntimeCommands.CommandsSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var commands = RestCommandBuilder.BuildCommands(submission, _userId);
        await FireAsync(AsyncSubmitPath, commands, cancellationToken).ConfigureAwait(false);
        return (RuntimeCommands.CommandId)commands.CommandId;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Fire-and-forget over one blocking <c>POST /v2/commands/async/submit-reassignment</c> call. A
    /// transport failure or non-success response throws <see cref="LedgerOperationException"/>; the
    /// resulting reassignment event is observed on <see cref="SubscribeAsync{T}"/>, not awaited
    /// here.
    /// </remarks>
    public async Task<RuntimeCommands.CommandId> SubmitReassignmentAsync(
        ReassignmentSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var commands = RestCommandBuilder.BuildReassignmentCommands(submission, _userId);
        await FireAsync(
            AsyncSubmitReassignmentPath,
            new Raw.SubmitReassignmentRequest { ReassignmentCommands = commands },
            cancellationToken).ConfigureAwait(false);
        return (RuntimeCommands.CommandId)commands.CommandId;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Submits over one blocking <c>POST /v2/commands/submit-and-wait-for-reassignment</c> call and
    /// projects the resulting reassignment into the typed <see cref="ContractStreamEvent{T}.Assigned"/>
    /// / <see cref="ContractStreamEvent{T}.Unassigned"/> variant. A structured error maps to a
    /// <see cref="ExerciseOutcome{T}.DamlError"/>; a transport failure, per-call
    /// <paramref name="timeout"/> overrun, or malformed response maps to an
    /// <see cref="ExerciseOutcome{T}.InfraError"/>, never a thrown exception.
    /// </remarks>
    public async Task<ExerciseOutcome<ContractStreamEvent<T>>> TrySubmitAndWaitForReassignmentAsync<T>(
        ReassignmentSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType
    {
        ArgumentNullException.ThrowIfNull(submission);

        var commands = RestCommandBuilder.BuildReassignmentCommands(submission, _userId);
        var submitter = new RuntimeCommands.SubmitterInfo(
            new HashSet<Party> { submission.Submitter }, new HashSet<Party>());
        var request = new Raw.SubmitAndWaitForReassignmentRequest
        {
            ReassignmentCommands = commands,
            EventFormat = RestSubscribeRequestBuilder.BuildReassignmentEventFormat<T>(submitter),
        };

        var client = _httpClientFactory.CreateClient(ServiceCollectionExtensions.HttpClientName);
        using var timeoutSource = CreateTimeoutSource(timeout, cancellationToken);
        var requestToken = timeoutSource?.Token ?? cancellationToken;

        HttpResponseMessage response;
        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, SubmitAndWaitForReassignmentPath)
            {
                Content = JsonContent.Create(request, options: RestRefitSettings.SerializerOptions),
            };
            response = await client.SendAsync(httpRequest, requestToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ExerciseOutcome<ContractStreamEvent<T>>.InfraError(
                (int)HttpStatusCode.RequestTimeout, $"Request exceeded the {DescribeDeadline(timeout)} deadline.");
        }
        catch (HttpRequestException transportFailure)
        {
            return new ExerciseOutcome<ContractStreamEvent<T>>.InfraError(
                (int)HttpStatusCode.ServiceUnavailable, transportFailure.Message);
        }

        using (response)
        {
            try
            {
                if (!response.IsSuccessStatusCode)
                {
                    var parsed = await RestErrorParser.ParseAsync(response, requestToken).ConfigureAwait(false);
                    return ToOutcome<ContractStreamEvent<T>>(parsed);
                }

                var body = await response.Content
                    .ReadFromJsonAsync<Raw.SubmitAndWaitForReassignmentResponse>(
                        RestRefitSettings.SerializerOptions, requestToken)
                    .ConfigureAwait(false);
                if (body?.Reassignment is null)
                {
                    return new ExerciseOutcome<ContractStreamEvent<T>>.InfraError(
                        (int)HttpStatusCode.InternalServerError,
                        "Server returned a successful response but no reassignment was present.");
                }

                ContractStreamEvent<T> projected;
                try
                {
                    projected = ProjectReassignmentResult<T>(body.Reassignment);
                }
                catch (Exception decodeFailure) when (decodeFailure is not OperationCanceledException)
                {
                    return new ExerciseOutcome<ContractStreamEvent<T>>.InfraError(
                        (int)HttpStatusCode.InternalServerError,
                        $"Could not decode the reassignment in the ledger response: {decodeFailure.Message}");
                }

                return new ExerciseOutcome<ContractStreamEvent<T>>.One(projected);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new ExerciseOutcome<ContractStreamEvent<T>>.InfraError(
                    (int)HttpStatusCode.RequestTimeout,
                    $"Request exceeded the {DescribeDeadline(timeout)} deadline while reading the response body.");
            }
            catch (JsonException malformedJson)
            {
                return new ExerciseOutcome<ContractStreamEvent<T>>.InfraError(
                    (int)HttpStatusCode.InternalServerError,
                    $"Server returned a malformed reassignment response body: {malformedJson.Message}");
            }
            catch (HttpRequestException transportFailure)
            {
                return new ExerciseOutcome<ContractStreamEvent<T>>.InfraError(
                    (int)HttpStatusCode.ServiceUnavailable, transportFailure.Message);
            }
        }
    }

    private ContractStreamEvent<T> ProjectReassignmentResult<T>(Raw.Reassignment reassignment)
        where T : IDamlType
    {
        var projected = ContractStreamProjector.ProjectReassignmentEvents<T>(reassignment, _logger).ToList();
        return projected.FirstOrDefault(e => e is ContractStreamEvent<T>.Assigned or ContractStreamEvent<T>.Unassigned)
            ?? projected.FirstOrDefault()
            ?? new ContractStreamEvent<T>.Unclassified(
                LedgerOffset.At(RestWireConversions.ParseOffset(reassignment.Offset)), UnclassifiedKind.EmptyReassignment);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One blocking <c>POST /v2/commands/completions</c> call whose success body is a JSON array of
    /// completion-stream responses — the same bounded-window shape <see cref="SubscribeAsync{T}"/>
    /// reads from <c>POST /v2/updates</c>. The participant closes the window once it has sent
    /// <see cref="RestLedgerClientOptions.CompletionStreamLimit"/> entries or once no completion has
    /// arrived for <see cref="RestLedgerClientOptions.CompletionStreamIdleTimeout"/>; leaving either
    /// unset defers to the participant's own settings. Enumeration therefore ends when the window
    /// closes rather than running forever — a caller following the stream reopens it from the last
    /// offset it observed, which the <see cref="CompletionStreamEvent.Checkpoint"/> entries keep
    /// advancing through quiet periods.
    /// <para>
    /// Fault contract: a non-success response ends the enumeration with a terminal
    /// <see cref="CompletionStreamEvent.StreamError"/> carrying the HTTP status code and the parsed
    /// participant message, matching the gRPC transport's in-band fault contract for this method
    /// rather than the throwing contract the bounded reads on this client use. A success body this
    /// client cannot read — malformed JSON, or a completion whose wire fields will not decode — ends
    /// the enumeration the same way, with a status code of <c>0</c> because the transport itself
    /// reported no failure. A transport failure that never reaches the participant still throws,
    /// since the opt-in retry pipeline classifies exceptions. A caller cancelling via
    /// <paramref name="cancellationToken"/> gets an <see cref="OperationCanceledException"/>, never a
    /// <see cref="CompletionStreamEvent.StreamError"/>.
    /// </para>
    /// </remarks>
    public IAsyncEnumerable<CompletionStreamEvent> CompletionStreamAsync(
        RuntimeCommands.SubmitterInfo submitter,
        long beginExclusiveOffset = 0L,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return CompletionStreamAsyncCore(submitter, beginExclusiveOffset, cancellationToken);
    }

    private async IAsyncEnumerable<CompletionStreamEvent> CompletionStreamAsyncCore(
        RuntimeCommands.SubmitterInfo submitter,
        long beginExclusiveOffset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = RestSubscribeRequestBuilder.BuildCompletionStreamRequest(
            submitter, beginExclusiveOffset, _userId);

        var client = _httpClientFactory.CreateClient(ServiceCollectionExtensions.HttpClientName);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildCompletionsPath())
        {
            Content = JsonContent.Create(request, options: RestRefitSettings.SerializerOptions),
        };
        using var response = await client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var parsed = await RestErrorParser.ParseAsync(response, cancellationToken).ConfigureAwait(false);
            LogCompletionStreamFailed(_logger, parsed.StatusCode, parsed.Message);
            yield return new CompletionStreamEvent.StreamError(parsed.StatusCode, parsed.Message);
            yield break;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var (entries, malformedBody) = ReadCompletionEntries(body);
        if (malformedBody is { } bodyError)
        {
            yield return bodyError;
            yield break;
        }

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ProjectCompletionEntry(entry) is not { } projected)
            {
                continue;
            }

            yield return projected;

            if (projected is CompletionStreamEvent.StreamError)
            {
                yield break;
            }
        }
    }

    private (IReadOnlyList<WireCompletionStreamResponse> Entries, CompletionStreamEvent.StreamError? MalformedBody)
        ReadCompletionEntries(string body)
    {
        try
        {
            return (RestStreamBodyReader.Parse<WireCompletionStreamResponse>(body), null);
        }
        catch (Exception decodeFailure) when (StreamEventClassifier.IsDecodeFailure(decodeFailure))
        {
            return ([], ToUndecodableBodyStreamError(decodeFailure));
        }
    }

    private CompletionStreamEvent? ProjectCompletionEntry(WireCompletionStreamResponse entry)
    {
        try
        {
            return ProjectCompletionResponse(entry.CompletionResponse);
        }
        catch (Exception decodeFailure) when (StreamEventClassifier.IsDecodeFailure(decodeFailure))
        {
            return ToUndecodableBodyStreamError(decodeFailure);
        }
    }

    private CompletionStreamEvent.StreamError ToUndecodableBodyStreamError(Exception decodeFailure)
    {
        LogCompletionStreamBodyUndecodable(_logger, decodeFailure);
        return new CompletionStreamEvent.StreamError(
            NoTransportFailureStatusCode,
            $"Could not decode the completion stream response body: {decodeFailure.Message}");
    }

    private CompletionStreamEvent? ProjectCompletionResponse(WireCompletionResponse? completionResponse)
    {
        if (completionResponse?.Completion is { } completion)
        {
            return RestCompletionProjector.Project(completion);
        }

        if (completionResponse?.OffsetCheckpoint is { } checkpoint)
        {
            return new CompletionStreamEvent.Checkpoint(RestWireConversions.ParseOffset(checkpoint.Offset));
        }

        var variant = completionResponse?.AdditionalProperties.Keys.FirstOrDefault() ?? "Unknown";
        LogCompletionStreamVariantSkipped(_logger, variant);
        return null;
    }

    private string BuildCompletionsPath()
    {
        var query = new List<string>(2);
        if (_completionStreamLimit is { } limit)
        {
            query.Add($"{LimitQueryParameter}={limit.ToString(CultureInfo.InvariantCulture)}");
        }
        if (_completionStreamIdleTimeout is { } idleTimeout)
        {
            var milliseconds = (long)idleTimeout.TotalMilliseconds;
            query.Add($"{StreamIdleTimeoutQueryParameter}={milliseconds.ToString(CultureInfo.InvariantCulture)}");
        }

        return query.Count == 0
            ? CompletionsPath
            : $"{CompletionsPath}?{string.Join('&', query)}";
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Completion stream request failed with status {StatusCode} — surfaced in-band as a terminal StreamError: {Detail}")]
    private static partial void LogCompletionStreamFailed(ILogger logger, int statusCode, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Completion stream response body could not be decoded — surfaced in-band as a terminal StreamError")]
    private static partial void LogCompletionStreamBodyUndecodable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Completion stream skipped variant {Variant}")]
    private static partial void LogCompletionStreamVariantSkipped(ILogger logger, string variant);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConnectedSynchronizer>> GetConnectedSynchronizersAsync(
        Party? party = null,
        string? participantId = null,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(ServiceCollectionExtensions.HttpClientName);
        using var response = await client
            .GetAsync(BuildConnectedSynchronizersPath(party, participantId), cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var body = await response.Content
            .ReadFromJsonAsync<Raw.GetConnectedSynchronizersResponse>(RestRefitSettings.SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        var synchronizers = body?.ConnectedSynchronizers ?? [];
        return synchronizers
            .Select(s => new ConnectedSynchronizer(s.SynchronizerAlias, s.SynchronizerId, MapPermission(s.Permission)))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<string> GetLedgerApiVersionAsync(CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(ServiceCollectionExtensions.HttpClientName);
        using var response = await client.GetAsync(LedgerApiVersionPath, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var body = await response.Content
            .ReadFromJsonAsync<Raw.GetLedgerApiVersionResponse>(RestRefitSettings.SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        return body?.Version
            ?? throw new LedgerOperationException(
                "Server returned a successful response but no version was present for the Ledger API version query.");
    }

    /// <inheritdoc />
    public Task<TransactionResult> GetUpdateByOffsetAsync(
        long offset,
        RuntimeCommands.SubmitterInfo submitter,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(offset);

        var request = new Raw.GetUpdateByOffsetRequest
        {
            Offset = offset.ToString(CultureInfo.InvariantCulture),
            UpdateFormat = RestSubscribeRequestBuilder.BuildTransactionUpdateFormat(submitter),
        };

        return GetUpdateAsync(
            UpdateByOffsetPath, request, $"offset {offset}", RestTransactionResultProjector.Project, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TransactionResult> GetUpdateByIdAsync(
        string updateId,
        RuntimeCommands.SubmitterInfo submitter,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(updateId);

        var request = new Raw.GetUpdateByIdRequest
        {
            UpdateId = updateId,
            UpdateFormat = RestSubscribeRequestBuilder.BuildTransactionUpdateFormat(submitter),
        };

        return GetUpdateAsync(
            UpdateByIdPath, request, $"id {updateId}", RestTransactionResultProjector.Project, cancellationToken);
    }

    private async Task<TProjection> GetUpdateAsync<TProjection>(
        string path,
        object request,
        string lookupDescription,
        Func<Raw.Transaction, TProjection> project,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(ServiceCollectionExtensions.HttpClientName);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(request, options: RestRefitSettings.SerializerOptions),
        };
        using var response = await client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var body = await response.Content
            .ReadFromJsonAsync<Raw.GetUpdateResponse>(RestRefitSettings.SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
        if (body is null)
        {
            throw new InvalidOperationException(
                $"Server returned a successful response but no update was present for {lookupDescription}.");
        }

        return ProjectPointRead(body, lookupDescription, project);
    }

    private static TProjection ProjectPointRead<TProjection>(
        Raw.GetUpdateResponse response,
        string lookupDescription,
        Func<Raw.Transaction, TProjection> project)
    {
        if (response.Update?.Transaction is not { } transaction)
        {
            var variant = response.Update?.Reassignment is not null
                ? "Reassignment"
                : response.Update?.TopologyTransaction is not null ? "TopologyTransaction" : "Unknown";
            throw new InvalidOperationException(
                $"Update at {lookupDescription} is a {variant}, not a Transaction; "
                + "point reads only project transaction-shaped updates.");
        }

        try
        {
            return project(transaction);
        }
        catch (Exception decodeFailure) when (
            decodeFailure is FormatException or JsonException or MalformedTransactionTreeException
            || RestTransactionResultProjector.IsMalformedResponse(decodeFailure))
        {
            var detail = decodeFailure.Message.StartsWith(RestTransactionResultProjector.MalformedResponsePrefix, StringComparison.Ordinal)
                ? decodeFailure.Message[RestTransactionResultProjector.MalformedResponsePrefix.Length..]
                : decodeFailure.Message;
            throw new InvalidOperationException(
                $"{RestTransactionResultProjector.MalformedResponsePrefix}the transaction at {lookupDescription} could not be decoded: {detail}",
                decodeFailure);
        }
    }

    private async Task FireAsync(string path, object body, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(ServiceCollectionExtensions.HttpClientName);

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(body, options: RestRefitSettings.SerializerOptions),
            };
            response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException transportFailure)
        {
            throw new LedgerOperationException(
                transportFailure.Message, (int)HttpStatusCode.ServiceUnavailable, transportFailure);
        }

        using (response)
        {
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string BuildConnectedSynchronizersPath(Party? party, string? participantId)
    {
        var query = new List<string>(2);
        if (party is { } requestedParty)
        {
            query.Add($"party={Uri.EscapeDataString(requestedParty.Id)}");
        }
        if (participantId is not null)
        {
            query.Add($"participantId={Uri.EscapeDataString(participantId)}");
        }

        return query.Count == 0
            ? ConnectedSynchronizersPath
            : $"{ConnectedSynchronizersPath}?{string.Join('&', query)}";
    }

    private static SynchronizerPermissionLevel MapPermission(
        Raw.GetConnectedSynchronizersResponse_ConnectedSynchronizerPermission? permission) => permission switch
    {
        null => SynchronizerPermissionLevel.Unspecified,
        Raw.GetConnectedSynchronizersResponse_ConnectedSynchronizerPermission.PARTICIPANT_PERMISSION_UNSPECIFIED
            => SynchronizerPermissionLevel.Unspecified,
        Raw.GetConnectedSynchronizersResponse_ConnectedSynchronizerPermission.PARTICIPANT_PERMISSION_SUBMISSION
            => SynchronizerPermissionLevel.Submission,
        Raw.GetConnectedSynchronizersResponse_ConnectedSynchronizerPermission.PARTICIPANT_PERMISSION_CONFIRMATION
            => SynchronizerPermissionLevel.Confirmation,
        Raw.GetConnectedSynchronizersResponse_ConnectedSynchronizerPermission.PARTICIPANT_PERMISSION_OBSERVATION
            => SynchronizerPermissionLevel.Observation,
        _ => SynchronizerPermissionLevel.Unrecognized,
    };
}
