// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Streams;
using Canton.Ledger.Kernel.Wire;
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

internal sealed partial class RestLedgerClient
{
    private const string AsyncSubmitPath = "/v2/commands/async/submit";
    private const string AsyncSubmitReassignmentPath = "/v2/commands/async/submit-reassignment";
    private const string SubmitAndWaitForReassignmentPath = "/v2/commands/submit-and-wait-for-reassignment";
    private const string ConnectedSynchronizersPath = "/v2/state/connected-synchronizers";
    private const string LedgerApiVersionPath = "/v2/version";
    private const string UpdateByOffsetPath = "/v2/updates/update-by-offset";
    private const string UpdateByIdPath = "/v2/updates/update-by-id";
    private const string CompletionsPath = "/v2/commands/completions";
    private const string MissingReassignmentMessage =
        "Server returned a successful response but no reassignment was present.";
    private const string MalformedReassignmentBodyPrefix =
        "Server returned a malformed reassignment response body: ";

    /// <inheritdoc />
    /// <remarks>
    /// Fire-and-forget over one blocking <c>POST /v2/commands/async/submit</c> call. A transport
    /// failure or non-success response throws <see cref="LedgerOperationException"/>; the verdict is
    /// observed on the completion stream, not awaited here.
    /// </remarks>
    public Task<RuntimeCommands.CommandId> SubmitAsync(
        RuntimeCommands.CommandsSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        return SubmitCoreAsync(submission, timeout, cancellationToken);
    }

    private async Task<RuntimeCommands.CommandId> SubmitCoreAsync(
        RuntimeCommands.CommandsSubmission submission,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var commands = RestCommandBuilder.BuildCommands(submission, _userId);
        await FireAsync(AsyncSubmitPath, commands, timeout, cancellationToken).ConfigureAwait(false);
        return (RuntimeCommands.CommandId)commands.CommandId;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Fire-and-forget over one blocking <c>POST /v2/commands/async/submit-reassignment</c> call. A
    /// transport failure or non-success response throws <see cref="LedgerOperationException"/>; the
    /// resulting reassignment event is observed on <see cref="SubscribeAsync{T}"/>, not awaited
    /// here.
    /// </remarks>
    public Task<RuntimeCommands.CommandId> SubmitReassignmentAsync(
        ReassignmentSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        return SubmitReassignmentCoreAsync(submission, timeout, cancellationToken);
    }

    private async Task<RuntimeCommands.CommandId> SubmitReassignmentCoreAsync(
        ReassignmentSubmission submission,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var commands = RestCommandBuilder.BuildReassignmentCommands(submission, _userId);
        await FireAsync(
            AsyncSubmitReassignmentPath,
            new Raw.SubmitReassignmentRequest { ReassignmentCommands = commands },
            timeout,
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
    /// <see cref="ExerciseOutcome{T}.InfraError"/>, never a thrown exception. Argument validation and
    /// request construction run before the call is issued, so they throw synchronously to the caller
    /// rather than through the returned <see cref="Task{TResult}"/>.
    /// </remarks>
    public Task<ExerciseOutcome<ContractStreamEvent<T>>> TrySubmitAndWaitForReassignmentAsync<T>(
        ReassignmentSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>
    {
        ArgumentNullException.ThrowIfNull(submission);

        var submitter = new RuntimeCommands.SubmitterInfo(
            new HashSet<Party> { submission.Submitter }, new HashSet<Party>());
        var request = new Raw.SubmitAndWaitForReassignmentRequest
        {
            ReassignmentCommands = RestCommandBuilder.BuildReassignmentCommands(submission, _userId),
            EventFormat = RestSubscribeRequestBuilder.BuildReassignmentEventFormat<T>(submitter),
        };

        return _calls.TrySendAsync<Raw.SubmitAndWaitForReassignmentResponse, ContractStreamEvent<T>>(
            new RestCall(
                HttpMethod.Post, SubmitAndWaitForReassignmentPath, request,
                MissingReassignmentMessage, MalformedReassignmentBodyPrefix),
            ProjectReassignmentOutcome<T>,
            timeout,
            cancellationToken);
    }

    private ExerciseOutcome<ContractStreamEvent<T>> ProjectReassignmentOutcome<T>(
        Raw.SubmitAndWaitForReassignmentResponse body)
        where T : ITemplate, IDamlRecord<T>
    {
        if (body.Reassignment is not { } reassignment)
        {
            return new ExerciseOutcome<ContractStreamEvent<T>>.InfraError(
                (int)HttpStatusCode.InternalServerError, MissingReassignmentMessage);
        }

        try
        {
            return new ExerciseOutcome<ContractStreamEvent<T>>.One(ProjectReassignmentResult<T>(reassignment));
        }
        catch (Exception decodeFailure) when (decodeFailure is not OperationCanceledException)
        {
            LogReassignmentUndecodable(_logger, decodeFailure);
            return new ExerciseOutcome<ContractStreamEvent<T>>.InfraError(
                (int)HttpStatusCode.InternalServerError,
                $"Could not decode the reassignment in the ledger response: {decodeFailure.Message}",
                SourceException: decodeFailure);
        }
    }

    private ContractStreamEvent<T> ProjectReassignmentResult<T>(Raw.Reassignment reassignment)
        where T : ITemplate, IDamlRecord<T>
    {
        var projected = ContractStreamProjector.ProjectReassignmentEvents<T>(reassignment, _logger).ToList();
        return projected.FirstOrDefault(e => e is ContractStreamEvent<T>.Assigned or ContractStreamEvent<T>.Unassigned)
            ?? projected.FirstOrDefault()
            ?? new ContractStreamEvent<T>.Unclassified(
                LedgerOffset.At(RestWireConversions.ParseOffset(reassignment.Offset)), UnclassifiedKind.EmptyReassignment);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A live tail over the same pagination loop the offset-range reads use: each
    /// <c>POST /v2/commands/completions</c> returns one bounded window, closed by the participant
    /// once it has sent <see cref="RestLedgerClientOptions.StreamWindowLimit"/> entries or once no
    /// completion has arrived for <see cref="RestLedgerClientOptions.StreamWindowIdleTimeout"/>, and
    /// the loop re-POSTs the next window from the last offset it observed for as long as the caller
    /// enumerates. A window that yields nothing is reopened at the same offset, so the enumeration
    /// does not end on a quiet ledger and a call site cannot tell this stream from the gRPC one.
    /// <para>
    /// The <see cref="CompletionStreamEvent.Checkpoint"/> entries are the participant's own offset
    /// checkpoints, relayed as they arrive; this client manufactures none, so a quiet stream leaves
    /// the caller's resume offset exactly where it stood.
    /// </para>
    /// <para>
    /// Fault contract: a non-success response ends the enumeration with a terminal
    /// <see cref="CompletionStreamEvent.StreamError"/> carrying the HTTP status code and the parsed
    /// participant message, matching the gRPC transport's in-band fault contract. A success body this
    /// client cannot read — malformed JSON, or a completion whose wire fields will not decode — ends
    /// the enumeration the same way, with a status code of <c>0</c> because the transport itself
    /// reported no failure, as does a window whose entries carry no offset the next window could
    /// resume from, since following it would re-read what was just delivered. A transport failure
    /// that never reaches the participant still throws, since the opt-in retry pipeline classifies
    /// exceptions. A caller cancelling via <paramref name="cancellationToken"/> gets an
    /// <see cref="OperationCanceledException"/>, never a
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
        var windows = ReadWindowsAsync<WireCompletionStreamResponse>(
            CompletionsPath,
            beginExclusiveOffset,
            endInclusive: null,
            beginExclusive => RestSubscribeRequestBuilder.BuildCompletionStreamRequest(
                submitter, beginExclusive, _userId),
            CompletionResumeOffset,
            cancellationToken);

        await foreach (var read in windows.ConfigureAwait(false))
        {
            if (read.Fault is { } fault)
            {
                yield return ToCompletionStreamError(fault);
                yield break;
            }

            if (read.Entry is not { } entry || ProjectCompletionEntry(entry) is not { } projected)
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

    private static long? CompletionResumeOffset(WireCompletionStreamResponse response)
    {
        var wireOffset = response.CompletionResponse?.Completion?.Offset
            ?? response.CompletionResponse?.OffsetCheckpoint?.Offset;

        return RestWireConversions.TryParseOffset(wireOffset, out var offset) ? offset : null;
    }

    private CompletionStreamEvent? ProjectCompletionEntry(WireCompletionStreamResponse entry)
    {
        try
        {
            return ProjectCompletionResponse(entry.CompletionResponse);
        }
        catch (Exception decodeFailure) when (StreamEventClassifier.IsNotCancellation(decodeFailure))
        {
            return ToCompletionStreamError(UndecodableBodyFault(decodeFailure));
        }
    }

    private StreamFault UndecodableBodyFault(Exception decodeFailure)
    {
        LogCompletionStreamBodyUndecodable(_logger, decodeFailure);
        return StreamFault.FromUndecodableBody(
            $"Could not decode the completion stream response body: {decodeFailure.Message}",
            decodeFailure);
    }

    private static CompletionStreamEvent.StreamError ToCompletionStreamError(StreamFault fault) =>
        new(fault.StatusCode, fault.Message, fault.Category, fault.ErrorId, fault.SourceException);

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Completion stream response body could not be decoded — surfaced in-band as a terminal StreamError")]
    private static partial void LogCompletionStreamBodyUndecodable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Completion stream skipped variant {Variant}")]
    private static partial void LogCompletionStreamVariantSkipped(ILogger logger, string variant);

    [LoggerMessage(Level = LogLevel.Error, Message = "The participant answered successfully, but the reassignment could not be decoded — surfaced as an InfraError outcome")]
    private static partial void LogReassignmentUndecodable(ILogger logger, Exception exception);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConnectedSynchronizer>> GetConnectedSynchronizersAsync(
        Party? party = null,
        string? participantId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var client = _calls.CreateClient();

        using var timeoutSource = RestCallEnvelope.CreateTimeoutSource(timeout, cancellationToken);
        var requestToken = timeoutSource?.Token ?? cancellationToken;

        using var response = await client
            .GetAsync(BuildConnectedSynchronizersPath(party, participantId), requestToken)
            .ConfigureAwait(false);
        await RestCallEnvelope.EnsureSuccessAsync(response, requestToken).ConfigureAwait(false);

        var body = await response.Content
            .ReadFromJsonAsync<Raw.GetConnectedSynchronizersResponse>(RestRefitSettings.SerializerOptions, requestToken)
            .ConfigureAwait(false);

        var synchronizers = body?.ConnectedSynchronizers ?? [];
        return synchronizers
            .Select(s => new ConnectedSynchronizer(s.SynchronizerAlias, s.SynchronizerId, MapPermission(s.Permission)))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<string> GetLedgerApiVersionAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var client = _calls.CreateClient();

        using var timeoutSource = RestCallEnvelope.CreateTimeoutSource(timeout, cancellationToken);
        var requestToken = timeoutSource?.Token ?? cancellationToken;

        using var response = await client.GetAsync(LedgerApiVersionPath, requestToken).ConfigureAwait(false);
        await RestCallEnvelope.EnsureSuccessAsync(response, requestToken).ConfigureAwait(false);

        var body = await response.Content
            .ReadFromJsonAsync<Raw.GetLedgerApiVersionResponse>(RestRefitSettings.SerializerOptions, requestToken)
            .ConfigureAwait(false);

        return body?.Version
            ?? throw new LedgerOperationException(
                "Server returned a successful response but no version was present for the Ledger API version query.");
    }

    /// <inheritdoc />
    public Task<TransactionResult> GetUpdateByOffsetAsync(
        long offset,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(offset);

        var request = new Raw.GetUpdateByOffsetRequest
        {
            Offset = offset.ToString(CultureInfo.InvariantCulture),
            UpdateFormat = RestSubscribeRequestBuilder.BuildTransactionUpdateFormat(submitter),
        };

        return GetUpdateAsync(
            UpdateByOffsetPath, request, $"offset {offset}", RestTransactionResultProjector.Project,
            timeout, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TransactionResult> GetUpdateByIdAsync(
        string updateId,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(updateId);

        var request = new Raw.GetUpdateByIdRequest
        {
            UpdateId = updateId,
            UpdateFormat = RestSubscribeRequestBuilder.BuildTransactionUpdateFormat(submitter),
        };

        return GetUpdateAsync(
            UpdateByIdPath, request, $"id {updateId}", RestTransactionResultProjector.Project,
            timeout, cancellationToken);
    }

    private async Task<TProjection> GetUpdateAsync<TProjection>(
        string path,
        object request,
        string lookupDescription,
        Func<Raw.Transaction, TProjection> project,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var client = _calls.CreateClient();

        using var timeoutSource = RestCallEnvelope.CreateTimeoutSource(timeout, cancellationToken);
        var requestToken = timeoutSource?.Token ?? cancellationToken;

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(request, options: RestRefitSettings.SerializerOptions),
        };
        using var response = await client.SendAsync(httpRequest, requestToken).ConfigureAwait(false);
        await RestCallEnvelope.EnsureSuccessAsync(response, requestToken).ConfigureAwait(false);

        var body = await response.Content
            .ReadFromJsonAsync<Raw.GetUpdateResponse>(RestRefitSettings.SerializerOptions, requestToken)
            .ConfigureAwait(false);
        if (body is null)
        {
            throw new InvalidOperationException(
                $"Server returned a successful response but no update was present for {lookupDescription}.");
        }

        return ProjectPointRead(body, lookupDescription, project);
    }

    internal static TProjection ProjectPointRead<TProjection>(
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
            decodeFailure is JsonException || MalformedResponse.IsWireDecodeFailure(decodeFailure))
        {
            throw MalformedResponse.CouldNotDecodeTransaction(lookupDescription, decodeFailure);
        }
    }

    private async Task FireAsync(string path, object body, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var client = _calls.CreateClient();

        using var timeoutSource = RestCallEnvelope.CreateTimeoutSource(timeout, cancellationToken);
        var requestToken = timeoutSource?.Token ?? cancellationToken;

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(body, options: RestRefitSettings.SerializerOptions),
            };
            response = await client.SendAsync(request, requestToken).ConfigureAwait(false);
        }
        catch (HttpRequestException transportFailure)
        {
            throw new LedgerOperationException(
                transportFailure.Message,
                (int)HttpStatusCode.ServiceUnavailable,
                innerException: transportFailure);
        }

        using (response)
        {
            await RestCallEnvelope.EnsureSuccessAsync(response, requestToken).ConfigureAwait(false);
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
