// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Telemetry;
using Com.Daml.Ledger.Api.V2;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Interactive = Com.Daml.Ledger.Api.V2.Interactive;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// Implementation of <see cref="ICantonLedgerClient"/> (and thus <see cref="ILedgerClient"/>)
/// using the Canton gRPC Ledger API.
/// </summary>
public sealed partial class LedgerClient : ICantonLedgerClient
{
    /// <summary>
    /// The <see cref="System.Diagnostics.ActivitySource"/> name used for OpenTelemetry tracing.
    /// Register with <c>tracing.AddSource(LedgerClient.ActivitySourceName)</c>.
    /// </summary>
    public static string ActivitySourceName => LedgerActivitySourceNames.GrpcLedgerClient;

    private readonly GrpcChannel _channel;
    private readonly LedgerCallInvoker _invoker;
    private readonly SubmissionClient _submissionClient;
    private readonly UpdateService.UpdateServiceClient _updateService;
    private readonly StateService.StateServiceClient _stateService;
    private readonly CommandCompletionService.CommandCompletionServiceClient _commandCompletionService;
    private readonly VersionService.VersionServiceClient _versionService;
    private readonly Interactive.InteractiveSubmissionService.InteractiveSubmissionServiceClient _interactiveSubmissionService;
    private readonly CommandBuilder _commandBuilder;
    private readonly LedgerClientOptions _options;
    private readonly ILogger<LedgerClient> _logger;
    private bool _disposed;

    /// <summary>
    /// Creates a new LedgerClient with the specified options and token provider.
    /// Logs are discarded unless a <paramref name="logger"/> is supplied.
    /// </summary>
    public LedgerClient(IOptions<LedgerClientOptions> options, ITokenProvider tokenProvider, ILogger<LedgerClient>? logger = null)
        : this(options.Value, tokenProvider, logger)
    {
    }

    /// <summary>
    /// Creates a new LedgerClient with the specified options and token provider.
    /// Logs are discarded unless a <paramref name="logger"/> is supplied.
    /// </summary>
    public LedgerClient(LedgerClientOptions options, ITokenProvider tokenProvider, ILogger<LedgerClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokenProvider);

        _options = options;
        _logger = logger ?? NullLogger<LedgerClient>.Instance;
        _channel = LedgerGrpcChannel.Create(_options);

        var commandService = new CommandService.CommandServiceClient(_channel);
        _updateService = new UpdateService.UpdateServiceClient(_channel);
        _stateService = new StateService.StateServiceClient(_channel);
        var commandSubmissionService = new CommandSubmissionService.CommandSubmissionServiceClient(_channel);
        _commandCompletionService = new CommandCompletionService.CommandCompletionServiceClient(_channel);
        _versionService = new VersionService.VersionServiceClient(_channel);
        _interactiveSubmissionService =
            new Interactive.InteractiveSubmissionService.InteractiveSubmissionServiceClient(_channel);

        _commandBuilder = new CommandBuilder(_options);
        _invoker = new LedgerCallInvoker(_options, tokenProvider);
        _submissionClient = new SubmissionClient(
            _invoker, commandService, commandSubmissionService, _commandBuilder, _options, _logger,
            GetUpdateByOffsetAsync, GetUpdateTreeByOffsetAsync);

        CallContextHelper.LogStartupDiagnostics(
            _logger, tokenProvider, _options.GrpcAddress, nameof(LedgerClient), "AddLedgerClient");
    }

    internal LedgerClient(
        LedgerClientOptions options,
        GrpcChannel channel,
        CommandService.CommandServiceClient commandService,
        ITokenProvider? tokenProvider = null,
        ILogger<LedgerClient>? logger = null)
        : this(
            options,
            channel,
            commandService,
            new UpdateService.UpdateServiceClient(channel),
            new StateService.StateServiceClient(channel),
            tokenProvider,
            logger)
    {
    }

    internal LedgerClient(
        LedgerClientOptions options,
        GrpcChannel channel,
        CommandService.CommandServiceClient commandService,
        UpdateService.UpdateServiceClient updateService,
        StateService.StateServiceClient stateService,
        ITokenProvider? tokenProvider = null,
        ILogger<LedgerClient>? logger = null)
        : this(
            options,
            channel,
            commandService,
            updateService,
            stateService,
            new CommandSubmissionService.CommandSubmissionServiceClient(channel),
            new CommandCompletionService.CommandCompletionServiceClient(channel),
            tokenProvider,
            logger: logger)
    {
    }

    internal LedgerClient(
        LedgerClientOptions options,
        GrpcChannel channel,
        CommandService.CommandServiceClient commandService,
        UpdateService.UpdateServiceClient updateService,
        StateService.StateServiceClient stateService,
        CommandSubmissionService.CommandSubmissionServiceClient commandSubmissionService,
        CommandCompletionService.CommandCompletionServiceClient commandCompletionService,
        ITokenProvider? tokenProvider = null,
        VersionService.VersionServiceClient? versionService = null,
        Interactive.InteractiveSubmissionService.InteractiveSubmissionServiceClient? interactiveSubmissionService = null,
        ILogger<LedgerClient>? logger = null)
    {
        _options = options;
        _channel = channel;
        _updateService = updateService;
        _stateService = stateService;
        _commandCompletionService = commandCompletionService;
        _versionService = versionService ?? new VersionService.VersionServiceClient(channel);
        _interactiveSubmissionService = interactiveSubmissionService
            ?? new Interactive.InteractiveSubmissionService.InteractiveSubmissionServiceClient(channel);
        _logger = logger ?? NullLogger<LedgerClient>.Instance;

        _commandBuilder = new CommandBuilder(_options);
        _invoker = new LedgerCallInvoker(_options, tokenProvider);
        _submissionClient = new SubmissionClient(
            _invoker, commandService, commandSubmissionService, _commandBuilder, _options, _logger,
            GetUpdateByOffsetAsync, GetUpdateTreeByOffsetAsync);
    }

    /// <inheritdoc />
    public Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        RuntimeCommands.ExerciseCommand command,
        RuntimeCommands.SubmitterInfo submitter,
        string? workflowId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        _submissionClient.TryExerciseAsync<TResult>(command, submitter, workflowId, timeout, cancellationToken);

    /// <inheritdoc />
    public Task<SubmitAndWaitResult> SubmitAndWaitAsync(
        RuntimeCommands.CommandsSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        _submissionClient.SubmitAndWaitAsync(submission, timeout, cancellationToken);

    /// <inheritdoc />
    public Task<SubmitAndWaitResult> SubmitAndWaitAsync(
        RuntimeCommands.CommandsSubmission submission,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        _submissionClient.SubmitAndWaitAsync(submission.WithSubmitter(submitter), timeout, cancellationToken);

    /// <inheritdoc />
    public Task<RuntimeCommands.CommandId> SubmitAsync(
        RuntimeCommands.CommandsSubmission submission,
        CancellationToken cancellationToken = default) =>
        _submissionClient.SubmitAsync(submission, cancellationToken);

    /// <inheritdoc />
    public Task<RuntimeCommands.CommandId> SubmitReassignmentAsync(
        ReassignmentSubmission submission,
        CancellationToken cancellationToken = default) =>
        _submissionClient.SubmitReassignmentAsync(submission, cancellationToken);

    /// <inheritdoc />
    public Task<ExerciseOutcome<Daml.Runtime.Streams.ContractStreamEvent<T>>> TrySubmitAndWaitForReassignmentAsync<T>(
        ReassignmentSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType =>
        _submissionClient.TrySubmitAndWaitForReassignmentAsync<T>(submission, timeout, cancellationToken);

    /// <inheritdoc />
    public Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
        RuntimeCommands.CommandsSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        _submissionClient.TrySubmitAndWaitForTransactionAsync(submission, timeout, cancellationToken);

    /// <inheritdoc />
    public Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
        RuntimeCommands.CommandsSubmission submission,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        _submissionClient.TrySubmitAndWaitForTransactionAsync(
            submission.WithSubmitter(submitter), timeout, cancellationToken);

    /// <inheritdoc />
    public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload,
        RuntimeCommands.SubmitterInfo submitter,
        string? workflowId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate =>
        _submissionClient.TryCreateAsync(payload, submitter, workflowId, timeout, cancellationToken);

    /// <inheritdoc />
    public Task<ExerciseOutcome<ContractId<TMarker>>> TryExerciseForCreatedAsync<TMarker>(
        RuntimeCommands.ExerciseCommand command,
        RuntimeCommands.SubmitterInfo submitter,
        string? workflowId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TMarker : IDamlType =>
        _submissionClient.TryExerciseForCreatedAsync<TMarker>(command, submitter, workflowId, timeout, cancellationToken);

    /// <summary>
    /// Creates a <see cref="CallInvoker"/> bound to this client's channel for driving raw generated
    /// gRPC stubs — services or overloads the typed surface does not cover — through the client's own
    /// authentication, deadline, and retry plumbing: construct any generated stub over it, e.g.
    /// <c>new StateService.StateServiceClient(client.CreateCallInvoker())</c>, and call it without
    /// building any <see cref="CallOptions"/> by hand.
    /// </summary>
    /// <remarks>
    /// A bearer token is resolved from the configured
    /// <see cref="Canton.Ledger.Abstractions.ITokenProvider"/> on every call;
    /// <see cref="Canton.Ledger.Abstractions.ITokenProvider.None"/> sends no
    /// <c>authorization</c> header, and a caller-supplied <c>authorization</c> metadata entry wins
    /// over the resolved token. Unary calls carry the configured
    /// <see cref="LedgerClientOptions.Timeout"/> as a per-attempt deadline when the caller sets none
    /// and run through the configured <see cref="LedgerClientOptions.Retry"/> pipeline, with auth
    /// headers and deadline recomputed on each attempt; a caller-supplied deadline is kept verbatim.
    /// Streaming calls attach auth headers but carry no default deadline — a server stream may
    /// legitimately outlive any unary budget — and are never retried. Because retried unary calls
    /// only surface the winning attempt, <c>AsyncUnaryCall&lt;TResponse&gt;.ResponseHeadersAsync</c>
    /// resolves only once the response itself is available — headers from a failed attempt never
    /// leak, but callers awaiting headers ahead of the body will wait for the body. The invoker
    /// stays valid until this client is disposed; dispose the client, not the invoker.
    /// </remarks>
    /// <returns>A <see cref="CallInvoker"/> that authenticated raw stubs can be constructed over.</returns>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    public CallInvoker CreateCallInvoker()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new AuthenticatedCallInvoker(_channel.CreateCallInvoker(), _invoker);
    }

    /// <summary>
    /// Shuts the underlying gRPC channel down asynchronously, then releases it.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        await _channel.ShutdownAsync().ConfigureAwait(false);
        _channel.Dispose();
    }

    /// <summary>
    /// Releases the underlying gRPC channel.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _channel.Dispose();
    }
}
