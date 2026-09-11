// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RuntimeCommands = Daml.Runtime.Commands;
using WireGetActiveContractsResponse = Canton.Ledger.Rest.Client.Raw.GetActiveContractsResponse;
using WireGetUpdatesResponse = Canton.Ledger.Rest.Client.Raw.GetUpdatesResponse;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Reads, writes, and streams participant ledger state over the Canton JSON Ledger API, exposing the
/// full Canton participant surface <see cref="Canton.Ledger.Abstractions.ICantonLedgerClient"/> — the
/// transport-neutral <see cref="ILedgerClient"/> trio (<see cref="ILedgerReader"/>,
/// <see cref="ILedgerWriter"/>, <see cref="ILedgerStreamer"/>) plus the Canton-specific operations
/// (fire submission, reassignment submissions, connected-synchronizer and Ledger API version
/// discovery, offset/id point reads). Requests go through the named <see cref="HttpClient"/>
/// registered as <see cref="ServiceCollectionExtensions.HttpClientName"/>, which carries the base
/// address and the bearer-auth and activity handlers.
/// </summary>
/// <remarks>
/// The offset-range reads (<see cref="SubscribeAsync{T}"/>,
/// <see cref="SubscribeLedgerEffectsAsync{T}"/>) run over a pagination loop: each window is one
/// blocking HTTP POST whose whole response is buffered, and the loop re-POSTs the next window from
/// the last offset it observed, stitching the windows into one continuous stream. A caller cannot
/// tell where one window ended and the next began, so an open-ended tail
/// (<c>toOffset = null</c>) and a bounded range are the same read with and without a termination
/// condition. A fault reaches the caller in band, as a terminal <c>StreamError</c>, never as a
/// throw — the contract every stream on the gRPC transport already honours. A caller cancelling
/// still gets an <see cref="OperationCanceledException"/>, and a transport failure that never
/// reached the participant still throws.
/// <see cref="Canton.Ledger.Abstractions.ICantonLedgerClient.CompletionStreamAsync"/> runs over that
/// same loop and is a live tail for the same reason.
/// <see cref="SubscribeActiveAsync{T}"/> is the one read that does not loop: the ACS snapshot is
/// taken at a single offset, and paging it belongs to a different endpoint.
/// </remarks>
internal sealed partial class RestLedgerClient
    : Canton.Ledger.Abstractions.ICantonLedgerClient, Canton.Ledger.Abstractions.IUnboundedStreamingCapability
{
    private const string LedgerEndPath = "/v2/state/ledger-end";
    private const string SubmitAndWaitPath = "/v2/commands/submit-and-wait";
    private const string SubmitAndWaitForTransactionPath = "/v2/commands/submit-and-wait-for-transaction";
    private const string ActiveContractsPath = "/v2/state/active-contracts";
    private const string UpdatesPath = "/v2/updates";

    private const long EmptyLedgerEndOffset = 0L;

    private const string MissingLedgerEndBodyMessage =
        "Server returned a successful response but no body was present for the ledger end.";
    private const string MissingSubmitAndWaitBodyMessage =
        "Server returned a successful response but no body was present for submit-and-wait.";
    private const string MalformedSubmitAndWaitBodyPrefix =
        "Server returned a malformed submit-and-wait response body: ";
    private const string MissingTransactionMessage =
        "Server returned a successful response but no transaction was present.";
    private const string MalformedTransactionPrefix =
        "Server returned a malformed transaction: ";

    private const string LimitQueryParameter = "limit";
    private const string StreamIdleTimeoutQueryParameter = "stream_idle_timeout_ms";

    private const long UnpacedWindowWarningThreshold = 100L;

    private const string UnresumableWindowMessage =
        "The stream window carried entries but no offset the next window could resume from, so " +
        "following it would re-read what was just delivered.";

    private const string WindowLimitHint =
        " The participant's entry cap is below the configured window limit; lower " +
        nameof(RestLedgerClientOptions) + "." + nameof(RestLedgerClientOptions.StreamWindowLimit) +
        " and resume from the last offset observed.";

    private readonly RestCallEnvelope _calls;
    private readonly string? _userId;
    private readonly long _streamWindowLimit;
    private readonly TimeSpan _streamWindowIdleTimeout;
    private readonly TimeSpan _shortestHonouredWindowHold;
    private readonly ILogger<RestLedgerClient> _logger;

    /// <inheritdoc />
    /// <remarks>
    /// <see langword="true"/>: <see cref="SubscribeAsync{T}"/> and
    /// <see cref="SubscribeLedgerEffectsAsync{T}"/> serve <c>toOffset: null</c> through the
    /// pagination loop, which re-POSTs a bounded window from the last offset it observed for as
    /// long as the caller enumerates.
    /// </remarks>
    public bool SupportsUnboundedStreaming => true;

    /// <summary>
    /// Initializes a new instance of the <see cref="RestLedgerClient"/> class with no configured
    /// user id; the participant derives it from the caller's access token.
    /// </summary>
    /// <param name="httpClientFactory">
    /// Factory for the named <see cref="HttpClient"/>
    /// (<see cref="ServiceCollectionExtensions.HttpClientName"/>) that the JSON Ledger API requests
    /// are issued through.
    /// </param>
    internal RestLedgerClient(IHttpClientFactory httpClientFactory)
        : this(httpClientFactory, options: null, logger: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RestLedgerClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">
    /// Factory for the named <see cref="HttpClient"/>
    /// (<see cref="ServiceCollectionExtensions.HttpClientName"/>) that the JSON Ledger API requests
    /// are issued through.
    /// </param>
    /// <param name="options">
    /// Options carrying the optional <see cref="RestLedgerClientOptions.UserId"/> sent on command
    /// submissions and the stream-window bounds every read is paged with. May be
    /// <see langword="null"/>, in which case no user id is sent and the window bounds take their
    /// documented defaults.
    /// </param>
    /// <param name="logger">
    /// Logger for diagnostics such as an unclassifiable <c>/v2/updates</c> variant. Defaults to
    /// <see cref="NullLogger{T}"/> when omitted.
    /// </param>
    internal RestLedgerClient(
        IHttpClientFactory httpClientFactory,
        IOptions<RestLedgerClientOptions>? options,
        ILogger<RestLedgerClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _userId = options?.Value.UserId;
        _streamWindowLimit = options?.Value.StreamWindowLimit ?? RestLedgerClientOptions.DefaultStreamWindowLimit;
        _streamWindowIdleTimeout =
            options?.Value.StreamWindowIdleTimeout ?? RestLedgerClientOptions.DefaultStreamWindowIdleTimeout;
        _shortestHonouredWindowHold = _streamWindowIdleTimeout / 2;
        _logger = logger ?? NullLogger<RestLedgerClient>.Instance;
        _calls = new RestCallEnvelope(httpClientFactory, _logger);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A non-success response is routed through the same error parser every other call on this
    /// client uses, so the participant's category, error id and message reach the caller as a
    /// <see cref="LedgerOperationException"/> rather than a bare transport failure.
    /// <para>
    /// The offset is decoded with the serializer settings the rest of this client uses, so both the
    /// proto3-canonical int64 string this endpoint's specification declares and the raw JSON number
    /// the participant emits today are accepted.
    /// </para>
    /// <para>
    /// A body that supplies no offset — the <c>offset</c> property absent, or present and
    /// explicitly <c>null</c> — reads as offset zero. Both are the participant declining to give a
    /// value rather than giving a bad one, and the served document declares the property optional
    /// and documents zero as the empty participant view of the ledger, leaving absence no other
    /// meaning. Supplying it therefore decodes what the participant stated by omission.
    /// </para>
    /// <para>
    /// An offset that is present as a value yet unusable — empty, negative or non-numeric — is the
    /// opposite case: nothing documents what it means, so inventing an offset there would hand the
    /// caller a silently wrong resumption point. It throws <see cref="LedgerOperationException"/>,
    /// as does a response with no body at all.
    /// </para>
    /// </remarks>
    public async Task<LedgerOffset> GetLedgerEndAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var client = _calls.CreateClient();

        using var timeoutSource = RestCallEnvelope.CreateTimeoutSource(timeout, cancellationToken);
        var requestToken = timeoutSource?.Token ?? cancellationToken;

        using var response = await client.GetAsync(LedgerEndPath, requestToken).ConfigureAwait(false);
        await RestCallEnvelope.EnsureSuccessAsync(response, requestToken).ConfigureAwait(false);

        var body = await response.Content
            .ReadFromJsonAsync<Raw.GetLedgerEndResponse>(RestRefitSettings.SerializerOptions, requestToken)
            .ConfigureAwait(false);

        if (body is null)
        {
            throw new LedgerOperationException(MissingLedgerEndBodyMessage);
        }

        if (body.Offset is null)
        {
            return LedgerOffset.At(EmptyLedgerEndOffset);
        }

        if (!RestWireConversions.TryParseOffset(body.Offset, out var offset))
        {
            throw new LedgerOperationException(
                "Server returned a successful response but the ledger end offset was not " +
                "a non-negative integer.");
        }

        return LedgerOffset.At(offset);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A bounded ACS snapshot over one window of <c>POST /v2/state/active-contracts</c>: the whole
    /// response is read before any entry is yielded, then the snapshot ends with a terminal
    /// <see cref="AcsSnapshotEntry{T}.Checkpoint"/> carrying the effective offset — even when the
    /// snapshot is empty — so a caller can resume <see cref="SubscribeAsync{T}"/> from it. A
    /// failure ends the snapshot with a terminal <see cref="AcsSnapshotEntry{T}.StreamError"/>
    /// instead, mutually exclusive with that checkpoint, so a caller is never handed a resume
    /// offset for a snapshot it did not receive in full. A 413 (past the participant's
    /// <c>http-list-max-elements-limit</c>) is one such failure and names the window limit to
    /// lower. Resolving the ledger end for a null <paramref name="activeAtOffset"/> happens before
    /// the snapshot begins, so a failure there still throws.
    /// </remarks>
    public IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsync<T>(
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T> =>
        SubscribeActiveAsyncCore<T>(submitter, activeAtOffset, cancellationToken);

    private async IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsyncCore<T>(
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? activeAtOffset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : ITemplate, IDamlRecord<T>
    {
        var effectiveOffset = activeAtOffset ?? await GetLedgerEndAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var request = RestSubscribeRequestBuilder.BuildGetActiveContractsRequest<T>(submitter, effectiveOffset.Value);

        var window = await ReadWindowAsync<WireGetActiveContractsResponse>(
            ActiveContractsPath, request, cancellationToken).ConfigureAwait(false);
        if (window.Fault is { } fault)
        {
            yield return new AcsSnapshotEntry<T>.StreamError(
                fault.StatusCode, fault.Message, fault.Category, fault.ErrorId, fault.SourceException);
            yield break;
        }

        foreach (var entry in window.Entries)
        {
            foreach (var projected in ContractStreamProjector.ProjectActiveContractEntry<T>(entry, _logger, effectiveOffset))
            {
                yield return ToAcsSnapshotEntry(projected);
            }
        }

        yield return new AcsSnapshotEntry<T>.Checkpoint(new StakeholderResume(effectiveOffset));
    }

    /// <inheritdoc />
    /// <remarks>
    /// An offset-range read over the pagination loop using the ACS-delta transaction shape. A
    /// <paramref name="toOffset"/> of <see langword="null"/> is an open-ended tail the loop follows
    /// for as long as the caller enumerates; a value is a termination condition on the same loop,
    /// so a range wider than the participant's entry cap pages instead of failing. An
    /// already-cancelled <paramref name="cancellationToken"/> is honored first, throwing
    /// <see cref="OperationCanceledException"/> before any request is sent. A failed window ends
    /// the enumeration with a terminal <see cref="ContractStreamEvent{T}.StreamError"/>.
    /// </remarks>
    public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>
    {
        cancellationToken.ThrowIfCancellationRequested();

        return SubscribeUpdatesAsyncCore<T>(submitter, fromOffset, toOffset, RestTransactionShape.AcsDelta, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The ledger-effects counterpart of <see cref="SubscribeAsync{T}"/>, over the same pagination
    /// loop and with the same open-ended, termination and fault behaviour.
    /// </remarks>
    public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>
    {
        cancellationToken.ThrowIfCancellationRequested();

        return SubscribeUpdatesAsyncCore<T>(submitter, fromOffset, toOffset, RestTransactionShape.LedgerEffects, cancellationToken);
    }

    private async IAsyncEnumerable<ContractStreamEvent<T>> SubscribeUpdatesAsyncCore<T>(
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? fromOffset,
        LedgerOffset? toOffset,
        RestTransactionShape shape,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : ITemplate, IDamlRecord<T>
    {
        var windows = ReadUpdateWindowsAsync<T>(submitter, fromOffset, toOffset, shape, cancellationToken);
        await foreach (var read in windows.ConfigureAwait(false))
        {
            if (read.Fault is { } fault)
            {
                yield return new ContractStreamEvent<T>.StreamError(
                    fault.StatusCode, fault.Message, fault.Category, fault.ErrorId, fault.SourceException);
                yield break;
            }

            if (read.Entry is not { } update)
            {
                continue;
            }

            foreach (var projected in ProjectUpdate<T>(update))
            {
                yield return projected;
            }
        }
    }

    private IAsyncEnumerable<StreamWindowRead<WireGetUpdatesResponse>> ReadUpdateWindowsAsync<T>(
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? fromOffset,
        LedgerOffset? toOffset,
        RestTransactionShape shape,
        CancellationToken cancellationToken)
        where T : IDamlType =>
        ReadWindowsAsync<WireGetUpdatesResponse>(
            UpdatesPath,
            fromOffset?.Value ?? 0L,
            toOffset?.Value,
            beginExclusive => RestSubscribeRequestBuilder.BuildGetUpdatesRequest<T>(
                submitter, beginExclusive, toOffset?.Value, shape),
            UpdateResumeOffset,
            cancellationToken);

    private static long? UpdateResumeOffset(WireGetUpdatesResponse response)
    {
        var wireOffset = response.Update?.Transaction?.Offset
            ?? response.Update?.Reassignment?.Offset
            ?? response.Update?.OffsetCheckpoint?.Offset
            ?? response.Update?.TopologyTransaction?.Offset;

        return RestWireConversions.TryParseOffset(wireOffset, out var offset) ? offset : null;
    }

    private IEnumerable<ContractStreamEvent<T>> ProjectUpdate<T>(WireGetUpdatesResponse update)
        where T : ITemplate, IDamlRecord<T>
    {
        if (update.Update?.Transaction is { } transaction)
        {
            foreach (var projected in ContractStreamProjector.ProjectTransactionEvents<T>(transaction, _logger))
            {
                yield return projected;
            }
        }
        else if (update.Update?.Reassignment is { } reassignment)
        {
            foreach (var projected in ContractStreamProjector.ProjectReassignmentEvents<T>(reassignment, _logger))
            {
                yield return projected;
            }
        }
        else if (update.Update?.OffsetCheckpoint is { } checkpoint)
        {
            yield return new ContractStreamEvent<T>.Checkpoint(LedgerOffset.At(RestWireConversions.ParseOffset(checkpoint.Offset)));
        }
        else
        {
            var variant = update.Update?.TopologyTransaction is not null ? nameof(update.Update.TopologyTransaction) : "Unknown";
            LogStreamVariantSkipped(_logger, typeof(T).Name, variant);
        }
    }

    private static AcsSnapshotEntry<T> ToAcsSnapshotEntry<T>(ContractStreamEvent<T> entry)
        where T : ITemplate, IDamlRecord<T> => entry switch
    {
        ContractStreamEvent<T>.Created created => new AcsSnapshotEntry<T>.Created(
            created.ContractId, created.Payload, created.Key, created.Offset, created.SynchronizerId, created.WitnessParties),
        ContractStreamEvent<T>.Unassigned unassigned => new AcsSnapshotEntry<T>.Unclassified(
            unassigned.Offset, UnclassifiedKind.UnassignedEvent),
        ContractStreamEvent<T>.Unclassified unclassified => new AcsSnapshotEntry<T>.Unclassified(
            unclassified.Offset, unclassified.Kind, unclassified.RawKind),
        _ => throw new InvalidOperationException(
            $"Active-contract snapshot produced an unexpected entry variant: {entry.GetType().Name}"),
    };

    private readonly record struct StreamWindow<TEntry>(IReadOnlyList<TEntry> Entries, StreamFault? Fault)
        where TEntry : class
    {
        internal static StreamWindow<TEntry> Failed(StreamFault fault) => new([], fault);
    }

    private readonly record struct StreamWindowRead<TEntry>(TEntry? Entry, StreamFault? Fault)
        where TEntry : class
    {
        internal static StreamWindowRead<TEntry> Of(TEntry entry) => new(entry, null);

        internal static StreamWindowRead<TEntry> Failed(StreamFault fault) => new(null, fault);
    }

    private async IAsyncEnumerable<StreamWindowRead<TEntry>> ReadWindowsAsync<TEntry>(
        string path,
        long beginExclusive,
        long? endInclusive,
        Func<long, object> buildRequest,
        Func<TEntry, long?> resumeOffsetOf,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TEntry : class
    {
        var windowUri = WindowPath(path);
        var resumeFrom = beginExclusive;
        var unpacedWindows = 0L;
        var warnAtConsecutiveWindows = UnpacedWindowWarningThreshold;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var openedFrom = resumeFrom;
            var openedAt = Stopwatch.GetTimestamp();
            var window = await ReadWindowAsync<TEntry>(windowUri, buildRequest(resumeFrom), cancellationToken)
                .ConfigureAwait(false);
            var participantHeldTheWindow = Stopwatch.GetElapsedTime(openedAt) >= _shortestHonouredWindowHold;
            if (window.Fault is { } fault)
            {
                yield return StreamWindowRead<TEntry>.Failed(fault);
                yield break;
            }

            var readAnOffset = false;
            foreach (var entry in window.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (resumeOffsetOf(entry) is { } observed)
                {
                    resumeFrom = observed;
                    readAnOffset = true;
                }

                yield return StreamWindowRead<TEntry>.Of(entry);
            }

            if (endInclusive is { } end && (window.Entries.Count < _streamWindowLimit || resumeFrom >= end))
            {
                yield break;
            }

            if (window.Entries.Count > 0 && !readAnOffset)
            {
                yield return StreamWindowRead<TEntry>.Failed(StreamFault.FromUndecodableBody(
                    UnresumableWindowMessage, new InvalidOperationException(UnresumableWindowMessage)));
                yield break;
            }

            if (participantHeldTheWindow || resumeFrom != openedFrom)
            {
                unpacedWindows = 0;
                warnAtConsecutiveWindows = UnpacedWindowWarningThreshold;
            }
            else if (++unpacedWindows >= warnAtConsecutiveWindows)
            {
                LogStreamWindowUnpaced(_logger, unpacedWindows, windowUri, resumeFrom);
                warnAtConsecutiveWindows *= 2;
            }
        }
    }

    private async Task<StreamWindow<TEntry>> ReadWindowAsync<TEntry>(
        string requestUri, object request, CancellationToken cancellationToken)
        where TEntry : class
    {
        var client = _calls.CreateClient();
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(request, options: RestRefitSettings.SerializerOptions),
        };
        using var response = await client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return StreamWindow<TEntry>.Failed(
                await WindowFaultAsync(response, cancellationToken).ConfigureAwait(false));
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!RestStreamBodyReader.TryParse<TEntry>(body, out var entries, out var decodeFailure))
        {
            LogStreamWindowBodyUndecodable(_logger, requestUri, decodeFailure);
            return StreamWindow<TEntry>.Failed(StreamFault.FromUndecodableBody(
                $"Could not decode the stream window response body: {decodeFailure.Message}", decodeFailure));
        }

        return new StreamWindow<TEntry>(entries, null);
    }

    private async Task<StreamFault> WindowFaultAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var parsed = await RestErrorParser.ParseAsync(response, cancellationToken).ConfigureAwait(false);
        var message = response.StatusCode == HttpStatusCode.RequestEntityTooLarge
            ? parsed.Message + WindowLimitHint
            : parsed.Message;

        LogStreamWindowFailed(_logger, parsed.StatusCode, message);
        return StreamFault.FromTransport(
            parsed.StatusCode, message, parsed.ClassifiedCategory, parsed.ReportedErrorId, sourceException: null);
    }

    /// <summary>
    /// The request URI for one window of a looped read: the endpoint plus the bounds that make the
    /// participant close the window. The ACS snapshot deliberately does not go through here. It is
    /// a single un-paged read, so an explicit <c>limit</c> would cap it at a window's worth of
    /// contracts and hand the caller a short snapshot that looks complete, where deferring to the
    /// participant's own cap makes an oversized snapshot a loud failure instead.
    /// </summary>
    private string WindowPath(string path) =>
        $"{path}?{LimitQueryParameter}={_streamWindowLimit.ToString(CultureInfo.InvariantCulture)}" +
        $"&{StreamIdleTimeoutQueryParameter}=" +
        ((long)_streamWindowIdleTimeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The transaction has zero or more than one exercised event for <paramref name="command"/>'s
    /// choice on a successful outcome (e.g. a nonconsuming choice that only forks other choices).
    /// The projection calls <see cref="TransactionResultExerciseExtensions.ExerciseResult{TReturn}(TransactionResult, string)"/>,
    /// which throws for those shapes rather than surfacing them through
    /// <see cref="ExerciseOutcome{T}"/>, so both transports raise the same failure.
    /// </exception>
    public Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        RuntimeCommands.ExerciseCommand command,
        RuntimeCommands.SubmitterInfo submitter,
        string? workflowId = null,
        RuntimeCommands.CommandId? commandId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return TryExerciseCoreAsync<TResult>(command, submitter, workflowId, commandId, timeout, cancellationToken);
    }

    private async Task<ExerciseOutcome<TResult>> TryExerciseCoreAsync<TResult>(
        RuntimeCommands.ExerciseCommand command,
        RuntimeCommands.SubmitterInfo submitter,
        string? workflowId,
        RuntimeCommands.CommandId? commandId,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var submission = NewSubmission(
            command, submitter, workflowId ?? $"exercise-{command.Choice.Value.ToLowerInvariant()}", commandId);
        var outcome = await TrySubmitAndWaitForTransactionCoreAsync(
                submission,
                RestSubscribeRequestBuilder.BuildTransactionFormat(submitter),
                wireTransaction => RestTransactionResultProjector.ProjectForChoiceResult<TResult>(
                    wireTransaction, command.Choice),
                timeout,
                cancellationToken)
            .ConfigureAwait(false);
        return RestTransactionResultProjector.ProjectChoiceResult<TResult>(outcome, command.Choice);
    }

    /// <inheritdoc />
    public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload,
        RuntimeCommands.SubmitterInfo submitter,
        string? workflowId = null,
        RuntimeCommands.CommandId? commandId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate
    {
        ArgumentNullException.ThrowIfNull(payload);

        return TryCreateCoreAsync(payload, submitter, workflowId, commandId, timeout, cancellationToken);
    }

    private async Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateCoreAsync<TTemplate>(
        TTemplate payload,
        RuntimeCommands.SubmitterInfo submitter,
        string? workflowId,
        RuntimeCommands.CommandId? commandId,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        where TTemplate : ITemplate
    {
        var createCommand = RuntimeCommands.CreateCommand.For(payload);
        var submission = NewSubmission(
            createCommand, submitter, workflowId ?? $"create-{typeof(TTemplate).Name.ToLowerInvariant()}", commandId);
        var outcome = await TrySubmitAndWaitForTransactionCoreAsync(
                submission,
                transactionFormat: null,
                RestTransactionResultProjector.ProjectForCreatedTemplate<TTemplate>,
                timeout,
                cancellationToken)
            .ConfigureAwait(false);
        return RestTransactionResultProjector.ProjectToContractId<TTemplate>(outcome);
    }

    /// <inheritdoc />
    public Task<SubmitAndWaitResult> SubmitAndWaitAsync(
        RuntimeCommands.CommandsSubmission submission,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        return SubmitAndWaitAsync(submission.WithSubmitter(submitter), timeout, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SubmitAndWaitResult> SubmitAndWaitAsync(
        RuntimeCommands.CommandsSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var commands = RestCommandBuilder.BuildCommands(submission, _userId);
        return _calls.SendAsync<Raw.SubmitAndWaitResponse, SubmitAndWaitResult>(
            new RestCall(
                HttpMethod.Post, SubmitAndWaitPath, commands,
                MissingSubmitAndWaitBodyMessage, MalformedSubmitAndWaitBodyPrefix),
            body => ProjectSubmitAndWaitResult(commands, body),
            timeout,
            cancellationToken);
    }

    private static SubmitAndWaitResult ProjectSubmitAndWaitResult(
        Raw.Commands commands, Raw.SubmitAndWaitResponse body)
    {
        if (!RestWireConversions.TryParseOffset(body.CompletionOffset, out var completionOffset))
        {
            throw new LedgerOperationException(
                "Server returned a successful response but the completion offset was missing or not " +
                "a non-negative integer for submit-and-wait.");
        }

        return new SubmitAndWaitResult(
            (RuntimeCommands.CommandId)commands.CommandId,
            body.UpdateId,
            LedgerOffset.At(completionOffset));
    }

    /// <inheritdoc />
    public Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
        RuntimeCommands.CommandsSubmission submission,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        return TrySubmitAndWaitForTransactionAsync(
            submission.WithSubmitter(submitter), timeout, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
        RuntimeCommands.CommandsSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        return TrySubmitAndWaitForTransactionCoreAsync(
            submission, transactionFormat: null, RestTransactionResultProjector.Project, timeout, cancellationToken);
    }

    private Task<ExerciseOutcome<TProjection>> TrySubmitAndWaitForTransactionCoreAsync<TProjection>(
        RuntimeCommands.CommandsSubmission submission,
        Raw.TransactionFormat? transactionFormat,
        Func<Raw.Transaction, TProjection> project,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var requestBody = new Raw.SubmitAndWaitForTransactionRequest
        {
            Commands = RestCommandBuilder.BuildCommands(submission, _userId),
        };
        if (transactionFormat is not null)
        {
            requestBody.TransactionFormat = transactionFormat;
        }

        return _calls.TrySendAsync<Raw.SubmitAndWaitForTransactionResponse, TProjection>(
            new RestCall(
                HttpMethod.Post, SubmitAndWaitForTransactionPath, requestBody,
                MissingTransactionMessage, MalformedTransactionPrefix),
            body => body.Transaction is { } transaction
                ? new ExerciseOutcome<TProjection>.One(project(transaction))
                : new ExerciseOutcome<TProjection>.InfraError(
                    (int)HttpStatusCode.InternalServerError, MissingTransactionMessage),
            timeout,
            cancellationToken);
    }

    private static RuntimeCommands.CommandsSubmission NewSubmission(
        RuntimeCommands.ICommand command,
        RuntimeCommands.SubmitterInfo submitter,
        string workflowId,
        RuntimeCommands.CommandId? commandId) =>
        RuntimeCommands.CommandsSubmission.Single(command)
            .WithSubmitter(submitter)
            .WithCommandId(commandId ?? new RuntimeCommands.CommandId(Guid.NewGuid().ToString()))
            .WithWorkflowId(new RuntimeCommands.WorkflowId(workflowId));

    /// <summary>
    /// No-op: <see cref="RestLedgerClient"/> holds no disposable resources of its own — its
    /// <see cref="HttpClient"/> instances come from the injected <see cref="IHttpClientFactory"/>,
    /// which owns their lifetime. <see cref="IAsyncDisposable.DisposeAsync"/> uses
    /// <see cref="ILedgerClient"/>'s default bridge to this method.
    /// </summary>
    public void Dispose()
    {
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Subscribe stream for {TemplateType} skipped variant {Variant}")]
    private static partial void LogStreamVariantSkipped(ILogger logger, string templateType, string variant);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stream window request failed with status {StatusCode} — surfaced in-band as a terminal StreamError: {Detail}")]
    private static partial void LogStreamWindowFailed(ILogger logger, int statusCode, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stream window response body from {Path} could not be decoded — surfaced in-band as a terminal StreamError")]
    private static partial void LogStreamWindowBodyUndecodable(ILogger logger, string path, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The participant answered {WindowCount} consecutive windows on {Path} from offset {Offset} " +
                  "without advancing it and without holding it for the stream_idle_timeout_ms it was sent; that " +
                  "hold is this stream's only pacing and the client adds none of its own")]
    private static partial void LogStreamWindowUnpaced(
        ILogger logger, long windowCount, string path, long offset);
}
