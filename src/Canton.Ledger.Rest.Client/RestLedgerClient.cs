// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Telemetry;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
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
/// The streaming reads (<see cref="SubscribeActiveAsync{T}"/>, <see cref="SubscribeAsync{T}"/>,
/// <see cref="SubscribeLedgerEffectsAsync{T}"/>) run over blocking HTTP POST — a single request
/// whose whole response is buffered before any element is yielded, unlike the gRPC transport's
/// true server streaming. That means a mid-read fault is never an in-band
/// <c>StreamError</c>: it either fails the one HTTP call (surfaced as a thrown exception before
/// the first yield) or it doesn't happen at all. An open-ended live tail (<c>toOffset = null</c>
/// on the offset-range reads) is the one read HTTP cannot serve; see
/// <see cref="SupportsUnboundedStreaming"/>.
/// <see cref="Canton.Ledger.Abstractions.ICantonLedgerClient.CompletionStreamAsync"/> runs over the
/// same blocking shape: <c>POST /v2/commands/completions</c> answers with a JSON array, so one call
/// yields the completions in one participant-bounded window rather than an endless tail, and a
/// caller follows the stream by reopening it from the last offset it observed.
/// </remarks>
public sealed partial class RestLedgerClient : Canton.Ledger.Abstractions.ICantonLedgerClient
{
    /// <summary>
    /// The <see cref="System.Diagnostics.ActivitySource"/> name used for OpenTelemetry tracing.
    /// Register with <c>tracing.AddSource(RestLedgerClient.ActivitySourceName)</c>.
    /// </summary>
    public static string ActivitySourceName => LedgerActivitySourceNames.RestLedgerClient;

    private const string LedgerEndPath = "/v2/state/ledger-end";
    private const string SubmitAndWaitPath = "/v2/commands/submit-and-wait";
    private const string SubmitAndWaitForTransactionPath = "/v2/commands/submit-and-wait-for-transaction";
    private const string ActiveContractsPath = "/v2/state/active-contracts";
    private const string UpdatesPath = "/v2/updates";

    private const long EmptyLedgerEndOffset = 0L;

    private const string UnboundedStreamingMessage =
        "RestLedgerClient cannot serve an open-ended live tail (toOffset: null) over blocking " +
        "HTTP POST /v2/updates. Supply an end offset for a bounded read, or use a future WebSocket " +
        "transport (see RestLedgerClient.SupportsUnboundedStreaming).";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string? _userId;
    private readonly long? _completionStreamLimit;
    private readonly TimeSpan? _completionStreamIdleTimeout;
    private readonly ILogger<RestLedgerClient> _logger;

    /// <summary>
    /// Capability probe for <see cref="ILedgerStreamer"/> consumers: always <see langword="false"/>
    /// today, because HTTP cannot serve an open-ended live tail
    /// (<see cref="SubscribeAsync{T}"/>/<see cref="SubscribeLedgerEffectsAsync{T}"/> with
    /// <c>toOffset: null</c>). Flips to <see langword="true"/> once a future WebSocket transport
    /// lands.
    /// </summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "An instance-level capability probe, by design (mirrors Stream.CanSeek); " +
            "flips per-instance once a future WebSocket transport is wired in.")]
    public bool SupportsUnboundedStreaming => false;

    /// <summary>
    /// Initializes a new instance of the <see cref="RestLedgerClient"/> class with no configured
    /// user id; the participant derives it from the caller's access token.
    /// </summary>
    /// <param name="httpClientFactory">
    /// Factory for the named <see cref="HttpClient"/>
    /// (<see cref="ServiceCollectionExtensions.HttpClientName"/>) that the JSON Ledger API requests
    /// are issued through.
    /// </param>
    public RestLedgerClient(IHttpClientFactory httpClientFactory)
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
    /// submissions. May be <see langword="null"/>, in which case no user id is sent.
    /// </param>
    /// <param name="logger">
    /// Logger for diagnostics such as an unclassifiable <c>/v2/updates</c> variant. Defaults to
    /// <see cref="NullLogger{T}"/> when omitted.
    /// </param>
    public RestLedgerClient(
        IHttpClientFactory httpClientFactory,
        IOptions<RestLedgerClientOptions>? options,
        ILogger<RestLedgerClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _httpClientFactory = httpClientFactory;
        _userId = options?.Value.UserId;
        _completionStreamLimit = options?.Value.CompletionStreamLimit;
        _completionStreamIdleTimeout = options?.Value.CompletionStreamIdleTimeout;
        _logger = logger ?? NullLogger<RestLedgerClient>.Instance;
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
        var client = _httpClientFactory.CreateClient(ServiceCollectionExtensions.HttpClientName);

        using var timeoutSource = CreateTimeoutSource(timeout, cancellationToken);
        var requestToken = timeoutSource?.Token ?? cancellationToken;

        using var response = await client
            .GetAsync(LedgerEndPath, requestToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, requestToken).ConfigureAwait(false);

        var body = await response.Content
            .ReadFromJsonAsync<Raw.GetLedgerEndResponse>(RestRefitSettings.SerializerOptions, requestToken)
            .ConfigureAwait(false);

        if (body is null)
        {
            throw new LedgerOperationException(
                "Server returned a successful response but no body was present for the ledger end.");
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
    /// A bounded ACS snapshot over one blocking <c>POST /v2/state/active-contracts</c> call: the
    /// whole response is read before any entry is yielded, then the snapshot ends with a terminal
    /// <see cref="AcsSnapshotEntry{T}.Checkpoint"/> carrying the effective offset — even when the
    /// snapshot is empty — so a caller can resume <see cref="SubscribeAsync{T}"/> from it. A 413
    /// response (past the participant's <c>http-list-max-elements-limit</c>) throws
    /// <see cref="LedgerResultTooLargeException"/>; any other non-success response throws
    /// <see cref="LedgerOperationException"/>. Both throw before the first yield, since the whole
    /// read is one blocking call — there is no in-band <c>StreamError</c> on this transport.
    /// </remarks>
    public IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsync<T>(
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType =>
        SubscribeActiveAsyncCore<T>(submitter, activeAtOffset, cancellationToken);

    private async IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsyncCore<T>(
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? activeAtOffset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : IDamlType
    {
        var effectiveOffset = activeAtOffset ?? await GetLedgerEndAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var request = RestSubscribeRequestBuilder.BuildGetActiveContractsRequest<T>(submitter, effectiveOffset.Value);

        var client = _httpClientFactory.CreateClient(ServiceCollectionExtensions.HttpClientName);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ActiveContractsPath)
        {
            Content = JsonContent.Create(request, options: RestRefitSettings.SerializerOptions),
        };
        using var response = await client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForBoundedReadFailureAsync(response, cancellationToken).ConfigureAwait(false);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        foreach (var entry in RestStreamBodyReader.Parse<WireGetActiveContractsResponse>(body))
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
    /// A bounded offset-range read over one blocking <c>POST /v2/updates</c> call using the
    /// ACS-delta transaction shape. <paramref name="toOffset"/> is required over this transport:
    /// <see langword="null"/> (an open-ended live tail) throws <see cref="NotSupportedException"/>
    /// — see <see cref="SupportsUnboundedStreaming"/>. An already-cancelled
    /// <paramref name="cancellationToken"/> is honored first, throwing
    /// <see cref="OperationCanceledException"/> ahead of any capability rejection. A 413 response
    /// throws <see cref="LedgerResultTooLargeException"/>; any other non-success response throws
    /// <see cref="LedgerOperationException"/>, both before the first yield.
    /// </remarks>
    public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (toOffset is not { } endInclusive)
        {
            throw new NotSupportedException(UnboundedStreamingMessage);
        }

        return SubscribeUpdatesAsyncCore<T>(submitter, fromOffset, endInclusive, RestTransactionShape.AcsDelta, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A bounded offset-range read over one blocking <c>POST /v2/updates</c> call using the
    /// ledger-effects transaction shape. <paramref name="toOffset"/> is required over this
    /// transport: <see langword="null"/> (an open-ended live tail) throws
    /// <see cref="NotSupportedException"/> — see <see cref="SupportsUnboundedStreaming"/>. An
    /// already-cancelled <paramref name="cancellationToken"/> is honored first, throwing
    /// <see cref="OperationCanceledException"/> ahead of any capability rejection. A 413
    /// response throws <see cref="LedgerResultTooLargeException"/>; any other non-success response
    /// throws <see cref="LedgerOperationException"/>, both before the first yield.
    /// </remarks>
    public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (toOffset is not { } endInclusive)
        {
            throw new NotSupportedException(UnboundedStreamingMessage);
        }

        return SubscribeUpdatesAsyncCore<T>(submitter, fromOffset, endInclusive, RestTransactionShape.LedgerEffects, cancellationToken);
    }

    private async IAsyncEnumerable<ContractStreamEvent<T>> SubscribeUpdatesAsyncCore<T>(
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? fromOffset,
        LedgerOffset toOffset,
        RestTransactionShape shape,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : IDamlType
    {
        var request = RestSubscribeRequestBuilder.BuildGetUpdatesRequest<T>(
            submitter, fromOffset?.Value ?? 0L, toOffset.Value, shape);

        var client = _httpClientFactory.CreateClient(ServiceCollectionExtensions.HttpClientName);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, UpdatesPath)
        {
            Content = JsonContent.Create(request, options: RestRefitSettings.SerializerOptions),
        };
        using var response = await client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForBoundedReadFailureAsync(response, cancellationToken).ConfigureAwait(false);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        foreach (var update in RestStreamBodyReader.Parse<WireGetUpdatesResponse>(body))
        {
            foreach (var projected in ProjectUpdate<T>(update))
            {
                yield return projected;
            }
        }
    }

    private IEnumerable<ContractStreamEvent<T>> ProjectUpdate<T>(WireGetUpdatesResponse update)
        where T : IDamlType
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
        where T : IDamlType => entry switch
    {
        ContractStreamEvent<T>.Created created => new AcsSnapshotEntry<T>.Created(
            created.ContractId, created.Payload, created.Offset, created.SynchronizerId, created.WitnessParties),
        ContractStreamEvent<T>.Unassigned unassigned => new AcsSnapshotEntry<T>.Unclassified(
            unassigned.Offset, UnclassifiedKind.UnassignedEvent.ToString()),
        ContractStreamEvent<T>.Unclassified unclassified => new AcsSnapshotEntry<T>.Unclassified(
            unclassified.Offset, unclassified.RawKind ?? unclassified.Kind.ToString()),
        _ => throw new InvalidOperationException(
            $"Active-contract snapshot produced an unexpected entry variant: {entry.GetType().Name}"),
    };

    private static async Task ThrowForBoundedReadFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new LedgerResultTooLargeException(
                $"The bounded read exceeded the participant's http-list-max-elements-limit " +
                $"(413 Content Too Large): {detail}");
        }

        var parsed = await RestErrorParser.ParseAsync(response, cancellationToken).ConfigureAwait(false);
        throw ToException(parsed);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The transaction has zero or more than one exercised event for <paramref name="command"/>'s
    /// choice on a successful outcome (e.g. a nonconsuming choice that only forks other choices).
    /// Matches the gRPC transport's <c>TransactionResultExerciseExtensions.ExerciseResult</c>
    /// contract, which throws for the same shapes rather than surfacing them through
    /// <see cref="ExerciseOutcome{T}"/>.
    /// </exception>
    public async Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        RuntimeCommands.ExerciseCommand command,
        RuntimeCommands.SubmitterInfo submitter,
        string? workflowId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var submission = NewSubmission(
            command, submitter, workflowId ?? $"exercise-{command.Choice.Value.ToLowerInvariant()}");
        var outcome = await TrySubmitAndWaitForTransactionCoreAsync(
                submission,
                RestSubscribeRequestBuilder.BuildTransactionFormat(submitter),
                RestTransactionResultProjector.Project,
                timeout,
                cancellationToken)
            .ConfigureAwait(false);
        return RestTransactionResultProjector.ProjectChoiceResult<TResult>(outcome, command.Choice);
    }

    /// <inheritdoc />
    public async Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload,
        RuntimeCommands.SubmitterInfo submitter,
        string? workflowId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate
    {
        ArgumentNullException.ThrowIfNull(payload);

        var createCommand = RuntimeCommands.CreateCommand.For(payload);
        var submission = NewSubmission(
            createCommand, submitter, workflowId ?? $"create-{typeof(TTemplate).Name.ToLowerInvariant()}");
        var outcome = await TrySubmitAndWaitForTransactionAsync(submission, timeout, cancellationToken)
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
    public async Task<SubmitAndWaitResult> SubmitAndWaitAsync(
        RuntimeCommands.CommandsSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var commands = RestCommandBuilder.BuildCommands(submission, _userId);
        var client = _httpClientFactory.CreateClient(ServiceCollectionExtensions.HttpClientName);

        using var timeoutSource = CreateTimeoutSource(timeout, cancellationToken);
        var requestToken = timeoutSource?.Token ?? cancellationToken;

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, SubmitAndWaitPath)
            {
                Content = JsonContent.Create(commands, options: RestRefitSettings.SerializerOptions),
            };
            response = await client.SendAsync(request, requestToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LedgerOperationException(
                $"Request exceeded the {DescribeDeadline(timeout)} deadline.", (int)HttpStatusCode.RequestTimeout, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new LedgerOperationException(ex.Message, (int)HttpStatusCode.ServiceUnavailable, ex);
        }

        using (response)
        {
            try
            {
                if (!response.IsSuccessStatusCode)
                {
                    var parsed = await RestErrorParser.ParseAsync(response, requestToken).ConfigureAwait(false);
                    throw ToException(parsed);
                }

                var body = await response.Content
                    .ReadFromJsonAsync<Raw.SubmitAndWaitResponse>(RestRefitSettings.SerializerOptions, requestToken)
                    .ConfigureAwait(false);
                if (body is null)
                {
                    throw new LedgerOperationException(
                        "Server returned a successful response but no body was present for submit-and-wait.");
                }

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
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new LedgerOperationException(
                    $"Request exceeded the {DescribeDeadline(timeout)} deadline while reading the response body.",
                    (int)HttpStatusCode.RequestTimeout, ex);
            }
            catch (HttpRequestException ex)
            {
                throw new LedgerOperationException(ex.Message, (int)HttpStatusCode.ServiceUnavailable, ex);
            }
            catch (JsonException ex)
            {
                throw new LedgerOperationException(
                    $"Server returned a malformed submit-and-wait response body: {ex.Message}", ex);
            }
        }
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

    private async Task<ExerciseOutcome<TProjection>> TrySubmitAndWaitForTransactionCoreAsync<TProjection>(
        RuntimeCommands.CommandsSubmission submission,
        Raw.TransactionFormat? transactionFormat,
        Func<Raw.Transaction, TProjection> project,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var commands = RestCommandBuilder.BuildCommands(submission, _userId);
        var client = _httpClientFactory.CreateClient(ServiceCollectionExtensions.HttpClientName);

        using var timeoutSource = CreateTimeoutSource(timeout, cancellationToken);
        var requestToken = timeoutSource?.Token ?? cancellationToken;

        HttpResponseMessage response;
        try
        {
            var requestBody = new Raw.SubmitAndWaitForTransactionRequest { Commands = commands };
            if (transactionFormat is not null)
            {
                requestBody.TransactionFormat = transactionFormat;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, SubmitAndWaitForTransactionPath)
            {
                Content = JsonContent.Create(requestBody, options: RestRefitSettings.SerializerOptions),
            };
            response = await client.SendAsync(request, requestToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ExerciseOutcome<TProjection>.InfraError(
                (int)HttpStatusCode.RequestTimeout, $"Request exceeded the {DescribeDeadline(timeout)} deadline.");
        }
        catch (HttpRequestException transportFailure)
        {
            return new ExerciseOutcome<TProjection>.InfraError(
                (int)HttpStatusCode.ServiceUnavailable, transportFailure.Message);
        }

        using (response)
        {
            try
            {
                if (!response.IsSuccessStatusCode)
                {
                    var parsed = await RestErrorParser.ParseAsync(response, requestToken).ConfigureAwait(false);
                    return ToOutcome<TProjection>(parsed);
                }

                var body = await response.Content
                    .ReadFromJsonAsync<Raw.SubmitAndWaitForTransactionResponse>(
                        RestRefitSettings.SerializerOptions, requestToken)
                    .ConfigureAwait(false);
                if (body?.Transaction is null)
                {
                    return new ExerciseOutcome<TProjection>.InfraError(
                        (int)HttpStatusCode.InternalServerError,
                        "Server returned a successful response but no transaction was present.");
                }

                return new ExerciseOutcome<TProjection>.One(project(body.Transaction));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new ExerciseOutcome<TProjection>.InfraError(
                    (int)HttpStatusCode.RequestTimeout,
                    $"Request exceeded the {DescribeDeadline(timeout)} deadline while reading the response body.");
            }
            catch (Exception malformed) when (
                malformed is FormatException or JsonException or MalformedTransactionTreeException
                || RestTransactionResultProjector.IsMalformedResponse(malformed))
            {
                LogTransactionResponseUndecodable(_logger, malformed);
                return new ExerciseOutcome<TProjection>.InfraError(
                    (int)HttpStatusCode.InternalServerError,
                    $"Server returned a malformed transaction: {malformed.Message}");
            }
            catch (HttpRequestException transportFailure)
            {
                return new ExerciseOutcome<TProjection>.InfraError(
                    (int)HttpStatusCode.ServiceUnavailable, transportFailure.Message);
            }
        }
    }

    private static string DescribeDeadline(TimeSpan? timeout) =>
        timeout is { } window ? window.ToString() : "HttpClient default";

    private static RuntimeCommands.CommandsSubmission NewSubmission(
        RuntimeCommands.ICommand command,
        RuntimeCommands.SubmitterInfo submitter,
        string workflowId) =>
        RuntimeCommands.CommandsSubmission.Single(command)
            .WithSubmitter(submitter)
            .WithCommandId(new RuntimeCommands.CommandId(Guid.NewGuid().ToString()))
            .WithWorkflowId(new RuntimeCommands.WorkflowId(workflowId));

    private static ExerciseOutcome<T> ToOutcome<T>(ParsedLedgerError parsed) =>
        parsed.ErrorId.Length > 0
            ? new ExerciseOutcome<T>.DamlError(parsed.Category, parsed.ErrorId, parsed.Message, parsed.Metadata)
            : new ExerciseOutcome<T>.InfraError(parsed.StatusCode, parsed.Message);

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var parsed = await RestErrorParser.ParseAsync(response, cancellationToken).ConfigureAwait(false);
        throw ToException(parsed);
    }

    private static LedgerOperationException ToException(ParsedLedgerError parsed) =>
        parsed.ErrorId.Length > 0
            ? new LedgerOperationException(parsed.Message, parsed.Category, parsed.ErrorId, parsed.Metadata)
            : new LedgerOperationException(parsed.Message, parsed.StatusCode);

    private static CancellationTokenSource? CreateTimeoutSource(TimeSpan? timeout, CancellationToken cancellationToken)
    {
        if (timeout is not { } window)
            return null;

        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(window);
        return source;
    }

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

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The submission committed, but the transaction in the participant's response could not be projected — surfaced as an InfraError outcome")]
    private static partial void LogTransactionResponseUndecodable(ILogger logger, Exception exception);
}
