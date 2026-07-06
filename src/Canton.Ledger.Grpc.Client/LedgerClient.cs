// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Resilience;
using Canton.Ledger.Kernel.Telemetry;
using Com.Daml.Ledger.Api.V2;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Grpc;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Peaceful.Extensions.Logging;
using Polly;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// Implementation of <see cref="ILedgerClient"/> using the Canton gRPC Ledger API.
/// </summary>
public sealed partial class LedgerClient : ILedgerClient
{
    /// <summary>
    /// The <see cref="ActivitySource"/> name used for OpenTelemetry tracing.
    /// Register with <c>tracing.AddSource(LedgerClient.ActivitySourceName)</c>.
    /// </summary>
    public static string ActivitySourceName => LedgerActivitySource.NameFor<LedgerClient>();

    private static readonly ActivitySource ActivitySource = LedgerActivitySource.Create<LedgerClient>();
    private static readonly ILogger<LedgerClient> Logger = StaticLoggerFactory.Create<LedgerClient>();

    private const string RetryAttemptActivityName = "LedgerClient.RetryAttempt";
    private const string RetryAttemptNumberTag = "retry.attempt";
    private const string RetryDelayTag = "retry.delay_ms";

    private readonly ResiliencePipeline _retryPipeline;
    private readonly GrpcChannel _channel;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly UpdateService.UpdateServiceClient _updateService;
    private readonly StateService.StateServiceClient _stateService;
    private readonly CommandSubmissionService.CommandSubmissionServiceClient _commandSubmissionService;
    private readonly CommandCompletionService.CommandCompletionServiceClient _commandCompletionService;
    private readonly VersionService.VersionServiceClient _versionService;
    private readonly LedgerClientOptions _options;
    private readonly ITokenProvider? _tokenProvider;
    private readonly string _serverAddress;
    private readonly int _serverPort;

    /// <summary>
    /// Creates a new LedgerClient with the specified options and token provider.
    /// </summary>
    public LedgerClient(IOptions<LedgerClientOptions> options, ITokenProvider tokenProvider)
        : this(options.Value, tokenProvider)
    {
    }

    /// <summary>
    /// Creates a new LedgerClient with the specified options and token provider.
    /// </summary>
    public LedgerClient(LedgerClientOptions options, ITokenProvider tokenProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokenProvider);

        _options = options;
        _tokenProvider = tokenProvider;
        _retryPipeline = RetryPipelineFactory.Create(_options.Retry, IsTransientRpcFailure, RecordRetryAttempt);
        (_serverAddress, _serverPort) = ActivityHelper.ParseServerEndpoint(_options.GrpcAddress);

        _channel = GrpcChannel.ForAddress(_options.GrpcAddress, new GrpcChannelOptions
        {
            MaxReceiveMessageSize = _options.MaxMessageSize,
            MaxSendMessageSize = _options.MaxMessageSize
        });

        _commandService = new CommandService.CommandServiceClient(_channel);
        _updateService = new UpdateService.UpdateServiceClient(_channel);
        _stateService = new StateService.StateServiceClient(_channel);
        _commandSubmissionService = new CommandSubmissionService.CommandSubmissionServiceClient(_channel);
        _commandCompletionService = new CommandCompletionService.CommandCompletionServiceClient(_channel);
        _versionService = new VersionService.VersionServiceClient(_channel);

        LogInitialized(Logger, _options.GrpcAddress);

        if (ReferenceEquals(_tokenProvider, ITokenProvider.None))
            LogUnauthenticatedMode(Logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "LedgerClient initialized with endpoint {Endpoint}")]
    private static partial void LogInitialized(ILogger logger, string endpoint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "LedgerClient running in unauthenticated mode. If this is unintentional, register an ITokenProvider or use the AddLedgerClient overload that accepts authConfiguration.")]
    private static partial void LogUnauthenticatedMode(ILogger logger);

    internal LedgerClient(
        LedgerClientOptions options,
        GrpcChannel channel,
        CommandService.CommandServiceClient commandService,
        ITokenProvider? tokenProvider = null)
        : this(
            options,
            channel,
            commandService,
            new UpdateService.UpdateServiceClient(channel),
            new StateService.StateServiceClient(channel),
            tokenProvider)
    {
    }

    internal LedgerClient(
        LedgerClientOptions options,
        GrpcChannel channel,
        CommandService.CommandServiceClient commandService,
        UpdateService.UpdateServiceClient updateService,
        StateService.StateServiceClient stateService,
        ITokenProvider? tokenProvider = null)
        : this(
            options,
            channel,
            commandService,
            updateService,
            stateService,
            new CommandSubmissionService.CommandSubmissionServiceClient(channel),
            new CommandCompletionService.CommandCompletionServiceClient(channel),
            tokenProvider)
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
        VersionService.VersionServiceClient? versionService = null)
    {
        _options = options;
        _channel = channel;
        _commandService = commandService;
        _updateService = updateService;
        _stateService = stateService;
        _commandSubmissionService = commandSubmissionService;
        _commandCompletionService = commandCompletionService;
        _tokenProvider = tokenProvider;
        _versionService = versionService ?? new VersionService.VersionServiceClient(channel);
        _retryPipeline = RetryPipelineFactory.Create(_options.Retry, IsTransientRpcFailure, RecordRetryAttempt);
        (_serverAddress, _serverPort) = ActivityHelper.ParseServerEndpoint(options.GrpcAddress);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Creating contract {TemplateType}")]
    private static partial void LogCreatingContract(ILogger logger, string templateType);

    /// <inheritdoc />
    public async Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        RuntimeCommands.ExerciseCommand command,
        RuntimeCommands.SubmitterInfo submitter,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = LedgerActivitySource.StartActivity<LedgerClient>(ActivitySource);
        activity.SetGrpcCallTags(CommandService.Descriptor, "SubmitAndWaitForTransaction", _serverAddress, _serverPort);
        var submission = NewExerciseSubmission(activity, command, submitter, workflowId);

        var transactionFormat = new TransactionFormat
        {
            TransactionShape = TransactionShape.LedgerEffects,
            EventFormat = new EventFormat { Verbose = true }
        };

        var commands = BuildCommands(submission);
        var outcome = await TrySubmitCoreAsync(
            commands, transactionFormat, propagateCancellation: true, cancellationToken);

        switch (outcome)
        {
            case ExerciseOutcome<TransactionResult>.One success:
                LogChoiceExercised(Logger, command.Choice, command.ContractId);
                return new ExerciseOutcome<TResult>.One(success.Result.ExerciseResult<TResult>(command.Choice));
            case ExerciseOutcome<TransactionResult>.DamlError damlError:
                LogChoiceExerciseFailed(Logger, command.Choice, command.ContractId);
                activity.RecordDamlError(damlError.ErrorId);
                return new ExerciseOutcome<TResult>.DamlError(
                    damlError.Category, damlError.ErrorId, damlError.Message, damlError.Metadata);
            case ExerciseOutcome<TransactionResult>.InfraError infraError:
                LogChoiceExerciseFailed(Logger, command.Choice, command.ContractId);
                activity.RecordInfraError(infraError.StatusCode, infraError.Message);
                return new ExerciseOutcome<TResult>.InfraError(infraError.StatusCode, infraError.Message);
            default:
                throw new InvalidOperationException($"Unhandled outcome: {outcome.GetType().Name}");
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Exercising choice {Choice} on {ContractId}")]
    private static partial void LogExercisingChoice(ILogger logger, RuntimeCommands.ChoiceName choice, ContractId contractId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Choice exercised: {Choice} on {ContractId}")]
    private static partial void LogChoiceExercised(ILogger logger, RuntimeCommands.ChoiceName choice, ContractId contractId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to exercise choice {Choice} on {ContractId}")]
    private static partial void LogChoiceExerciseFailed(ILogger logger, RuntimeCommands.ChoiceName choice, ContractId contractId);

    /// <inheritdoc />
    public async Task<SubmitAndWaitResult> SubmitAndWaitAsync(
        RuntimeCommands.CommandsSubmission submission,
        CancellationToken cancellationToken = default)
    {
        var commands = BuildCommands(submission);
        var response = await SubmitAndWaitCoreAsync(commands, cancellationToken);
        return new SubmitAndWaitResult(
            (RuntimeCommands.CommandId)commands.CommandId,
            response.UpdateId,
            response.CompletionOffset);
    }

    /// <summary>
    /// Fire-and-forget submission: hands the commands to the participant and
    /// returns once they are accepted for processing, yielding the
    /// <c>command_id</c> for correlating the eventual completion. The verdict
    /// on the transaction itself is not awaited here — observe it on
    /// <see cref="CompletionStreamAsync"/>.
    /// </summary>
    /// <remarks>
    /// A true fire path with no client-side pending-set (ADR 0007): the consumer
    /// correlates completions by <c>command_id</c>/<c>submission_id</c> and owns
    /// its own offset. The returned <see cref="RuntimeCommands.CommandId"/> is the
    /// effective id the participant recorded — minted here when the submission
    /// omits one — so a caller retrying after a transport failure (by hand or via
    /// a resilience policy such as Polly) must resubmit with this same id for
    /// ledger-side deduplication. Re-invoking with a fresh, command_id-less
    /// submission instead mints a new id and double-submits, because the
    /// participant may have accepted the first attempt before the failure
    /// surfaced.
    /// </remarks>
    public async Task<RuntimeCommands.CommandId> Submit(
        RuntimeCommands.CommandsSubmission submission,
        CancellationToken cancellationToken = default)
    {
        using var activity = LedgerActivitySource.StartActivity<LedgerClient>(ActivitySource);
        activity.SetGrpcCallTags(CommandSubmissionService.Descriptor, "Submit", _serverAddress, _serverPort);
        var commands = BuildCommands(submission);
        LogFireSubmit(Logger, commands.CommandId, submission.Commands.Count);

        var request = new SubmitRequest { Commands = commands };
        try
        {
            await InvokeAsync(
                (headers, deadline, token) => _commandSubmissionService.SubmitAsync(request, headers, deadline, token),
                cancellationToken);
        }
        catch (RpcException ex)
        {
            activity.RecordGrpcError(ex);
            throw;
        }

        return (RuntimeCommands.CommandId)commands.CommandId;
    }

    /// <summary>
    /// Streams command completions for the submitter's parties as they arrive,
    /// surfacing the raw <see cref="Completion"/> primitive (ADR 0007). The
    /// consumer correlates each completion by <c>command_id</c>/<c>submission_id</c>
    /// and persists its own offset; the client holds no correlation or recovery
    /// state. To catch the completion of a command you are about to
    /// <see cref="Submit"/>, capture the offset before submitting and pass it as
    /// <paramref name="beginExclusiveOffset"/> — a completion can be emitted
    /// before the stream is opened.
    /// </summary>
    public IAsyncEnumerable<Completion> CompletionStreamAsync(
        RuntimeCommands.SubmitterInfo submitter,
        long beginExclusiveOffset = 0L,
        CancellationToken cancellationToken = default) =>
        CompletionStreamAsyncCore(submitter, beginExclusiveOffset, cancellationToken);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fire submit of {CommandCount} commands (command_id {CommandId})")]
    private static partial void LogFireSubmit(ILogger logger, string commandId, int commandCount);

    /// <inheritdoc />
    public async Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
        RuntimeCommands.CommandsSubmission submission,
        CancellationToken cancellationToken = default)
    {
        using var activity = LedgerActivitySource.StartActivity<LedgerClient>(ActivitySource);
        activity.SetGrpcCallTags(CommandService.Descriptor, "SubmitAndWaitForTransaction", _serverAddress, _serverPort);
        LogSubmittingCommands(Logger, submission.Commands.Count);

        var commands = BuildCommands(submission);
        // Plain request: no TransactionFormat yields the server-default AcsDelta shape.
        var outcome = await TrySubmitCoreAsync(
            commands, transactionFormat: null, propagateCancellation: false, cancellationToken);

        switch (outcome)
        {
            case ExerciseOutcome<TransactionResult>.DamlError damlError:
                activity.RecordDamlError(damlError.ErrorId);
                break;
            case ExerciseOutcome<TransactionResult>.InfraError infraError:
                activity.RecordInfraError(infraError.StatusCode, infraError.Message);
                break;
        }

        return outcome;
    }

    private async Task<ExerciseOutcome<TransactionResult>> TrySubmitCoreAsync(
        Commands commands,
        TransactionFormat? transactionFormat,
        bool propagateCancellation,
        CancellationToken cancellationToken)
    {
        var request = new SubmitAndWaitForTransactionRequest { Commands = commands };
        if (transactionFormat is not null)
        {
            request.TransactionFormat = transactionFormat;
        }

        try
        {
            var response = await InvokeAsync(
                (headers, deadline, token) =>
                    _commandService.SubmitAndWaitForTransactionAsync(request, headers, deadline, token),
                cancellationToken);

            var transactionResult = TransactionResultProjector.Project(response);
            LogTransactionCompleted(
                Logger,
                transactionResult.UpdateId,
                transactionResult.CreatedContracts.Count,
                transactionResult.ArchivedContractIds.Count);
            return new ExerciseOutcome<TransactionResult>.One(transactionResult);
        }
        catch (RpcException ex)
        {
            // A caller-cancelled exercise should surface as cancellation, not a mapped
            // InfraError; the plain submit path keeps its historical map-everything behavior.
            if (propagateCancellation && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            // Distinguish structured Daml errors (rich error model) from infra failures.
            // If the trailer carries an ErrorInfo we treat it as a Daml error, even on
            // status codes that aren't intrinsically Canton (server choice).
            LogSubmitFailed(Logger, ex.StatusCode, ex.Status.Detail);
            var (category, errorId, message, metadata) = DamlErrorParser.Parse(ex);
            if (errorId.Length > 0)
            {
                return new ExerciseOutcome<TransactionResult>.DamlError(category, errorId, message, metadata);
            }

            return new ExerciseOutcome<TransactionResult>.InfraError((int)ex.StatusCode, ex.Status.Detail ?? ex.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Submit failed: {StatusCode} — {Detail}")]
    private static partial void LogSubmitFailed(ILogger logger, StatusCode statusCode, string? detail);

    /// <inheritdoc />
    public async Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload,
        RuntimeCommands.SubmitterInfo submitter,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate
    {
        using var activity = LedgerActivitySource.StartActivity<LedgerClient>(ActivitySource, ActivityKind.Internal);
        activity?.SetTag(LedgerClientActivityTags.DamlTemplateId, typeof(TTemplate).Name);
        SetSubmitterTags(activity, submitter);

        var createCommand = RuntimeCommands.CreateCommand.For(payload);
        var submission = NewSubmission(
            createCommand, submitter, workflowId ?? $"create-{typeof(TTemplate).Name.ToLowerInvariant()}");

        LogCreatingContract(Logger, typeof(TTemplate).Name);

        var outcome = await TrySubmitAndWaitForTransactionAsync(submission, cancellationToken);
        return TransactionResultProjector.ProjectToContractId<TTemplate>(outcome);
    }

    /// <inheritdoc />
    public async Task<ExerciseOutcome<ContractId<TMarker>>> TryExerciseForCreatedAsync<TMarker>(
        RuntimeCommands.ExerciseCommand command,
        RuntimeCommands.SubmitterInfo submitter,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
        where TMarker : IDamlType
    {
        using var activity = LedgerActivitySource.StartActivity<LedgerClient>(ActivitySource, ActivityKind.Internal);
        activity?.SetTag(LedgerClientActivityTags.DamlTemplateId, typeof(TMarker).Name);
        var submission = NewExerciseSubmission(activity, command, submitter, workflowId);

        var outcome = await TrySubmitAndWaitForTransactionAsync(submission, cancellationToken);
        return TransactionResultProjector.ProjectToContractId<TMarker>(outcome);
    }

    private static RuntimeCommands.CommandsSubmission NewSubmission(
        RuntimeCommands.ICommand command,
        RuntimeCommands.SubmitterInfo submitter,
        string workflowId) =>
        RuntimeCommands.CommandsSubmission.Single(command)
            .WithSubmitter(submitter)
            .WithCommandId(new RuntimeCommands.CommandId(Guid.NewGuid().ToString()))
            .WithWorkflowId(new RuntimeCommands.WorkflowId(workflowId));

    private static RuntimeCommands.CommandsSubmission NewExerciseSubmission(
        Activity? activity,
        RuntimeCommands.ExerciseCommand command,
        RuntimeCommands.SubmitterInfo submitter,
        string? workflowId)
    {
        activity?.SetTag(LedgerClientActivityTags.DamlChoice, command.Choice.Value);
        activity?.SetTag(LedgerClientActivityTags.DamlContractId, command.ContractId.Value);
        SetSubmitterTags(activity, submitter);
        LogExercisingChoice(Logger, command.Choice, command.ContractId);
        return NewSubmission(
            command, submitter, workflowId ?? $"exercise-{command.Choice.Value.ToLowerInvariant()}");
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Submitting {CommandCount} commands")]
    private static partial void LogSubmittingCommands(ILogger logger, int commandCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transaction completed: {UpdateId}, Created: {CreatedCount}, Archived: {ArchivedCount}")]
    private static partial void LogTransactionCompleted(ILogger logger, string updateId, int createdCount, int archivedCount);

    private async Task<SubmitAndWaitResponse> SubmitAndWaitCoreAsync(
        Commands commands,
        CancellationToken cancellationToken)
    {
        var request = new SubmitAndWaitRequest { Commands = commands };

        return await InvokeAsync(
            (headers, deadline, token) => _commandService.SubmitAndWaitAsync(request, headers, deadline, token),
            cancellationToken);
    }

    internal Commands BuildCommands(RuntimeCommands.CommandsSubmission submission)
    {
        var commands = new Commands
        {
            CommandId = submission.CommandId?.Value ?? Guid.NewGuid().ToString(),
            WorkflowId = submission.WorkflowId?.Value ?? string.Empty,
        };

        if (_options.UserId is not null)
        {
            commands.UserId = _options.UserId;
        }

        if (submission.ActAs is not null)
        {
            commands.ActAs.AddRange(submission.ActAs.Select(p => p.Id));
        }

        if (submission.ReadAs is not null)
        {
            commands.ReadAs.AddRange(submission.ReadAs.Select(p => p.Id));
        }

        if (submission.SynchronizerId is { } synchronizerId)
        {
            commands.SynchronizerId = synchronizerId.Id;
        }

        foreach (var cmd in submission.Commands)
        {
            var protoCommand = cmd switch
            {
                RuntimeCommands.CreateCommand create => new Command
                {
                    Create = new CreateCommand
                    {
                        TemplateId = DamlValueConverter.ToProtoIdentifier(create.TemplateId),
                        CreateArguments = DamlValueConverter.ToProtoRecord(create.CreateArguments)
                    }
                },
                RuntimeCommands.ExerciseCommand exercise => new Command
                {
                    Exercise = new ExerciseCommand
                    {
                        TemplateId = DamlValueConverter.ToProtoIdentifier(exercise.TemplateId),
                        ContractId = exercise.ContractId.Value,
                        Choice = exercise.Choice.Value,
                        ChoiceArgument = DamlValueConverter.ToProtoValue(exercise.ChoiceArgument)
                    }
                },
                _ => throw new NotSupportedException($"Command type {cmd.GetType().Name} is not supported")
            };

            commands.Commands_.Add(protoCommand);
        }

        return commands;
    }

    private Task<Metadata?> GetHeadersAsync(CancellationToken cancellationToken) =>
        AuthHeaderHelper.GetHeadersAsync(_tokenProvider, cancellationToken);

    private DateTime? GetDeadline()
    {
        if (_options.Timeout == null)
            return null;

        return DateTime.UtcNow.Add(_options.Timeout.Value);
    }

    /// <summary>
    /// Runs a single unary RPC through the retry pipeline, wrapping only the transport call so
    /// command construction (which fixes a stable <c>command_id</c>) stays above the retry boundary.
    /// Auth headers and the per-attempt deadline are recomputed on each attempt, so every retry is
    /// granted a fresh <see cref="LedgerClientOptions.Timeout"/> budget rather than sharing one budget
    /// across the whole sequence. With retry disabled (the default) the pipeline is empty and the call
    /// runs exactly once. The caller's <paramref name="cancellationToken"/> halts retries promptly.
    /// </summary>
    private ValueTask<TResponse> InvokeAsync<TResponse>(
        Func<Metadata?, DateTime?, CancellationToken, AsyncUnaryCall<TResponse>> call,
        CancellationToken cancellationToken) =>
        _retryPipeline.ExecuteAsync(
            async token =>
            {
                var headers = await GetHeadersAsync(token);
                return await call(headers, GetDeadline(), token);
            },
            cancellationToken);

    private static bool IsTransientRpcFailure(Exception exception) =>
        exception is RpcException { StatusCode: StatusCode.Unavailable or StatusCode.DeadlineExceeded };

    private static void RecordRetryAttempt(RetryAttempt attempt)
    {
        using var activity = ActivitySource.StartActivity(RetryAttemptActivityName, ActivityKind.Internal);
        if (activity is null) return;

        activity.SetTag(RetryAttemptNumberTag, attempt.AttemptNumber);
        activity.SetTag(RetryDelayTag, attempt.RetryDelay.TotalMilliseconds);
        if (attempt.Exception is RpcException rpcException)
        {
            activity.SetStatus(ActivityStatusCode.Error, rpcException.Status.Detail);
            activity.SetTag(ActivityHelper.RpcGrpcStatusCode, (int)rpcException.StatusCode);
            activity.SetTag(ActivityHelper.ErrorType, rpcException.StatusCode.ToString());
        }
    }

    private async IAsyncEnumerable<Completion> CompletionStreamAsyncCore(
        RuntimeCommands.SubmitterInfo submitter,
        long beginExclusiveOffset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = LedgerActivitySource.StartActivity<LedgerClient>(ActivitySource);
        activity.SetGrpcCallTags(CommandCompletionService.Descriptor, "CompletionStream", _serverAddress, _serverPort);
        activity?.SetTag(LedgerClientActivityTags.CantonFromOffset, beginExclusiveOffset);
        SetSubmitterTags(activity, submitter);

        var request = BuildCompletionStreamRequest(submitter, beginExclusiveOffset);
        LogCompletionStreamStarted(Logger, beginExclusiveOffset);

        using var call = _commandCompletionService.CompletionStream(
            request,
            headers: await GetHeadersAsync(cancellationToken),
            deadline: null,
            cancellationToken: cancellationToken);

        var stream = call.ResponseStream;

        while (true)
        {
            var step = await StreamMoveResult.NextAsync(stream, cancellationToken);
            if (step.Faulted is { } fault)
            {
                LogCompletionStreamError(Logger, fault.StatusCode, fault.Status.Detail);
                activity.RecordGrpcError(fault);
                throw fault;
            }

            if (!step.Moved) yield break;

            if (stream.Current.CompletionResponseCase
                == CompletionStreamResponse.CompletionResponseOneofCase.Completion)
            {
                yield return stream.Current.Completion;
            }
        }
    }

    private CompletionStreamRequest BuildCompletionStreamRequest(
        RuntimeCommands.SubmitterInfo submitter,
        long beginExclusiveOffset)
    {
        var request = new CompletionStreamRequest { BeginExclusive = beginExclusiveOffset };
        if (_options.UserId is not null)
        {
            request.UserId = _options.UserId;
        }

        request.Parties.AddRange(CompletionParties(submitter));
        return request;
    }

    private static IEnumerable<string> CompletionParties(RuntimeCommands.SubmitterInfo submitter)
    {
        var seen = new HashSet<string>();
        foreach (var party in submitter.ActAs)
        {
            if (seen.Add(party.Id)) yield return party.Id;
        }

        foreach (var party in submitter.ReadAs)
        {
            if (seen.Add(party.Id)) yield return party.Id;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Completion stream started from offset {BeginExclusiveOffset}")]
    private static partial void LogCompletionStreamStarted(ILogger logger, long beginExclusiveOffset);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Completion stream failed: {StatusCode} {Detail}")]
    private static partial void LogCompletionStreamError(ILogger logger, StatusCode statusCode, string? detail);

    /// <inheritdoc />
    public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        RuntimeCommands.SubmitterInfo submitter,
        long? fromOffset = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType
    {
        var filterId = MarkerMatcher<T>.StreamFilterIdentifier();
        return SubscribeAsyncCore<T>(submitter, filterId, fromOffset, cancellationToken);
    }

    private async IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsyncCore<T>(
        RuntimeCommands.SubmitterInfo submitter,
        ProtoIdentifier filterId,
        long? fromOffset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : IDamlType
    {
        using var activity = LedgerActivitySource.StartActivity<LedgerClient>(ActivitySource);
        activity.SetGrpcCallTags(UpdateService.Descriptor, "GetUpdates", _serverAddress, _serverPort);
        activity?.SetTag(LedgerClientActivityTags.DamlTemplateId, typeof(T).Name);
        activity?.SetTag(LedgerClientActivityTags.CantonFromOffset, fromOffset);
        SetSubmitterTags(activity, submitter);

        var request = SubscribeRequestBuilder.BuildGetUpdatesRequest(
            submitter,
            filterId,
            fromOffset,
            MarkerMatcher<T>.IsInterface);

        LogSubscribeStarted(Logger, typeof(T).Name, fromOffset ?? 0L);

        using var call = _updateService.GetUpdates(
            request,
            headers: await GetHeadersAsync(cancellationToken),
            deadline: null,
            cancellationToken: cancellationToken);

        var stream = call.ResponseStream;

        while (true)
        {
            var step = await StreamMoveResult.NextAsync(stream, cancellationToken);
            if (step.Faulted is { } fault)
            {
                LogSubscribeStreamError(Logger, typeof(T).Name, fault.StatusCode, fault.Status.Detail);
                activity.RecordGrpcError(fault);
                yield return new ContractStreamEvent<T>.StreamError(
                    (int)fault.StatusCode,
                    fault.Status.Detail ?? fault.Message);
                yield break;
            }

            if (!step.Moved) yield break;

            foreach (var typedEvent in ProjectUpdate<T>(stream.Current))
            {
                yield return typedEvent;
            }
        }
    }

    private static IEnumerable<ContractStreamEvent<T>> ProjectUpdate<T>(
        GetUpdatesResponse response)
        where T : IDamlType
    {
        switch (response.UpdateCase)
        {
            case GetUpdatesResponse.UpdateOneofCase.Transaction:
                foreach (var typedEvent in ContractStreamProjector.ProjectTransactionEvents<T>(response.Transaction))
                {
                    yield return typedEvent;
                }
                break;
            case GetUpdatesResponse.UpdateOneofCase.OffsetCheckpoint:
                yield return new ContractStreamEvent<T>.Checkpoint(response.OffsetCheckpoint.Offset);
                break;
            case GetUpdatesResponse.UpdateOneofCase.Reassignment:
                foreach (var typedEvent in ContractStreamProjector.ProjectReassignmentEvents<T>(response.Reassignment))
                {
                    yield return typedEvent;
                }
                break;
            default:
                LogStreamVariantSkipped(Logger, typeof(T).Name, response.UpdateCase);
                break;
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeActiveAsync<T>(
        RuntimeCommands.SubmitterInfo submitter,
        CancellationToken cancellationToken = default)
        where T : IDamlType
    {
        var templateFilter = MarkerMatcher<T>.StreamFilterIdentifier();
        return SubscribeActiveAsyncCore<T>(submitter, templateFilter, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> GetLedgerEndAsync(CancellationToken cancellationToken = default)
    {
        using var activity = LedgerActivitySource.StartActivity<LedgerClient>(ActivitySource);
        activity.SetGrpcCallTags(StateService.Descriptor, "GetLedgerEnd", _serverAddress, _serverPort);

        try
        {
            var response = await InvokeAsync(
                (headers, deadline, token) => _stateService.GetLedgerEndAsync(new GetLedgerEndRequest(), headers, deadline, token),
                cancellationToken);
            activity?.SetTag(LedgerClientActivityTags.CantonOffset, response.Offset);
            return response.Offset;
        }
        catch (RpcException ex)
        {
            activity.RecordGrpcError(ex);
            throw;
        }
    }

    /// <summary>
    /// Returns the synchronizers the participant is currently connected to.
    /// </summary>
    /// <param name="party">
    /// Optional party whose connection permissions scope the result. Null returns
    /// every synchronizer the participant is connected to, with an unspecified
    /// permission on each entry.
    /// </param>
    /// <param name="participantId">
    /// Optional participant id, for a participant querying another participant's
    /// mapping. Null defaults to the host participant.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<ConnectedSynchronizer>> GetConnectedSynchronizersAsync(
        Party? party = null,
        string? participantId = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = LedgerActivitySource.StartActivity<LedgerClient>(ActivitySource);
        activity.SetGrpcCallTags(StateService.Descriptor, "GetConnectedSynchronizers", _serverAddress, _serverPort);

        var request = new GetConnectedSynchronizersRequest();
        if (party is { } requestedParty)
        {
            request.Party = requestedParty.Id;
            activity?.SetTag(LedgerClientActivityTags.CantonPartyId, requestedParty.Id);
        }

        if (participantId is not null)
        {
            request.ParticipantId = participantId;
            activity?.SetTag(LedgerClientActivityTags.CantonParticipantId, participantId);
        }

        try
        {
            var response = await InvokeAsync(
                (headers, deadline, token) => _stateService.GetConnectedSynchronizersAsync(request, headers, deadline, token),
                cancellationToken);

            return response.ConnectedSynchronizers
                .Select(s => new ConnectedSynchronizer(s.SynchronizerAlias, s.SynchronizerId, s.Permission.ToString()))
                .ToList();
        }
        catch (RpcException ex)
        {
            activity.RecordGrpcError(ex);
            throw;
        }
    }

    /// <summary>
    /// Returns the Ledger API version reported by the participant.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string> GetLedgerApiVersionAsync(CancellationToken cancellationToken = default)
    {
        using var activity = LedgerActivitySource.StartActivity<LedgerClient>(ActivitySource);
        activity.SetGrpcCallTags(VersionService.Descriptor, "GetLedgerApiVersion", _serverAddress, _serverPort);

        try
        {
            var response = await InvokeAsync(
                (headers, deadline, token) => _versionService.GetLedgerApiVersionAsync(new GetLedgerApiVersionRequest(), headers, deadline, token),
                cancellationToken);
            return response.Version;
        }
        catch (RpcException ex)
        {
            activity.RecordGrpcError(ex);
            throw;
        }
    }

    /// <summary>
    /// Looks up a single update by its absolute offset, projected the same way as
    /// <see cref="TrySubmitAndWaitForTransactionAsync"/>'s success case. The
    /// <paramref name="submitter"/>'s combined <c>ActAs ∪ ReadAs</c> parties scope
    /// visibility, with no template/interface restriction — every event those
    /// parties witness on the update is returned.
    /// </summary>
    /// <param name="offset">The absolute offset of the update to look up. Must be positive.</param>
    /// <param name="submitter">The parties whose visibility scopes the lookup.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="offset"/> is zero or negative.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The update at <paramref name="offset"/> is a reassignment or topology
    /// transaction rather than a ledger transaction.
    /// </exception>
    public async Task<TransactionResult> GetUpdateByOffsetAsync(
        long offset,
        RuntimeCommands.SubmitterInfo submitter,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(offset);

        using var activity = LedgerActivitySource.StartActivity<LedgerClient>(ActivitySource);
        activity.SetGrpcCallTags(UpdateService.Descriptor, "GetUpdateByOffset", _serverAddress, _serverPort);
        activity?.SetTag(LedgerClientActivityTags.CantonOffset, offset);
        SetSubmitterTags(activity, submitter);

        var request = new GetUpdateByOffsetRequest
        {
            Offset = offset,
            UpdateFormat = SubscribeRequestBuilder.BuildTransactionUpdateFormat(submitter),
        };

        try
        {
            var response = await InvokeAsync(
                (headers, deadline, token) => _updateService.GetUpdateByOffsetAsync(request, headers, deadline, token),
                cancellationToken);

            return ProjectPointReadTransaction(response, $"offset {offset}");
        }
        catch (RpcException ex)
        {
            activity.RecordGrpcError(ex);
            throw;
        }
    }

    /// <summary>
    /// Looks up a single update by its update id, projected the same way as
    /// <see cref="TrySubmitAndWaitForTransactionAsync"/>'s success case. The
    /// <paramref name="submitter"/>'s combined <c>ActAs ∪ ReadAs</c> parties scope
    /// visibility, with no template/interface restriction — every event those
    /// parties witness on the update is returned.
    /// </summary>
    /// <param name="updateId">The id of the update to look up.</param>
    /// <param name="submitter">The parties whose visibility scopes the lookup.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// The update with <paramref name="updateId"/> is a reassignment or topology
    /// transaction rather than a ledger transaction.
    /// </exception>
    public async Task<TransactionResult> GetUpdateByIdAsync(
        string updateId,
        RuntimeCommands.SubmitterInfo submitter,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(updateId);

        using var activity = LedgerActivitySource.StartActivity<LedgerClient>(ActivitySource);
        activity.SetGrpcCallTags(UpdateService.Descriptor, "GetUpdateById", _serverAddress, _serverPort);
        activity?.SetTag(LedgerClientActivityTags.CantonUpdateId, updateId);
        SetSubmitterTags(activity, submitter);

        var request = new GetUpdateByIdRequest
        {
            UpdateId = updateId,
            UpdateFormat = SubscribeRequestBuilder.BuildTransactionUpdateFormat(submitter),
        };

        try
        {
            var response = await InvokeAsync(
                (headers, deadline, token) => _updateService.GetUpdateByIdAsync(request, headers, deadline, token),
                cancellationToken);

            return ProjectPointReadTransaction(response, $"id {updateId}");
        }
        catch (RpcException ex)
        {
            activity.RecordGrpcError(ex);
            throw;
        }
    }

    private static TransactionResult ProjectPointReadTransaction(GetUpdateResponse response, string lookupDescription)
    {
        if (response.UpdateCase != GetUpdateResponse.UpdateOneofCase.Transaction)
        {
            throw new InvalidOperationException(
                $"Update at {lookupDescription} is a {response.UpdateCase}, not a Transaction; "
                + "point reads only project transaction-shaped updates.");
        }

        return TransactionResultProjector.Project(response.Transaction);
    }

    private async IAsyncEnumerable<ContractStreamEvent<T>> SubscribeActiveAsyncCore<T>(
        RuntimeCommands.SubmitterInfo submitter,
        ProtoIdentifier templateFilter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : IDamlType
    {
        using var activity = LedgerActivitySource.StartActivity<LedgerClient>(ActivitySource);
        activity.SetGrpcCallTags(StateService.Descriptor, "GetActiveContracts", _serverAddress, _serverPort);
        activity?.SetTag(LedgerClientActivityTags.DamlTemplateId, typeof(T).Name);
        SetSubmitterTags(activity, submitter);

        var ledgerEnd = await InvokeAsync(
            (headers, deadline, token) => _stateService.GetLedgerEndAsync(new GetLedgerEndRequest(), headers, deadline, token),
            cancellationToken);
        var sharedHeaders = await GetHeadersAsync(cancellationToken);

        var request = SubscribeRequestBuilder.BuildGetActiveContractsRequest(
            submitter,
            templateFilter,
            ledgerEnd.Offset,
            MarkerMatcher<T>.IsInterface);

        LogSubscribeActiveStarted(Logger, typeof(T).Name, ledgerEnd.Offset);

        using var call = _stateService.GetActiveContracts(
            request,
            headers: sharedHeaders,
            deadline: null,
            cancellationToken: cancellationToken);

        var stream = call.ResponseStream;

        while (true)
        {
            var step = await StreamMoveResult.NextAsync(stream, cancellationToken);
            if (step.Faulted is { } fault)
            {
                activity.RecordGrpcError(fault);
                RethrowActiveContractsStreamFault<T>(fault);
            }

            if (!step.Moved) yield break;

            var projected = ContractStreamProjector.ProjectActiveContractEntry<T>(stream.Current);
            if (projected is ContractStreamEvent<T>.Unclassified unclassified)
            {
                LogActiveContractEntryUnclassified(Logger, typeof(T).Name, stream.Current.ContractEntryCase, unclassified.Kind);
            }
            yield return projected;
        }
    }

    private static void SetSubmitterTags(Activity? activity, RuntimeCommands.SubmitterInfo submitter)
    {
        if (activity is null) return;
        activity.SetTag(LedgerClientActivityTags.CantonSubmitterActAs, string.Join(",", submitter.ActAs.Select(p => p.Id)));
        if (submitter.ReadAs.Count > 0)
        {
            activity.SetTag(LedgerClientActivityTags.CantonSubmitterReadAs, string.Join(",", submitter.ReadAs.Select(p => p.Id)));
        }
    }

    private static void RethrowActiveContractsStreamFault<T>(RpcException fault)
        where T : IDamlType
    {
        LogSubscribeStreamError(Logger, typeof(T).Name, fault.StatusCode, fault.Status.Detail);
        throw fault;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Subscribing to {TemplateType} updates from offset {FromOffset}")]
    private static partial void LogSubscribeStarted(ILogger logger, string templateType, long fromOffset);

    [LoggerMessage(Level = LogLevel.Information, Message = "Subscribing to active {TemplateType} contracts at offset {AtOffset}")]
    private static partial void LogSubscribeActiveStarted(ILogger logger, string templateType, long atOffset);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Subscribe stream failed for {TemplateType}: {StatusCode} {Detail}")]
    private static partial void LogSubscribeStreamError(ILogger logger, string templateType, StatusCode statusCode, string detail);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Subscribe stream for {TemplateType} skipped variant {Variant}")]
    private static partial void LogStreamVariantSkipped(ILogger logger, string templateType, GetUpdatesResponse.UpdateOneofCase variant);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Active contracts snapshot for {TemplateType} could not classify entry {ContractEntryCase} — surfaced as Unclassified ({Kind})")]
    private static partial void LogActiveContractEntryUnclassified(ILogger logger, string templateType, GetActiveContractsResponse.ContractEntryOneofCase contractEntryCase, string kind);

    /// <summary>
    /// Releases the underlying gRPC channel.
    /// </summary>
    public void Dispose()
    {
        _channel?.Dispose();
    }
}
