// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Canton.Ledger.Auth;
using Com.Daml.Ledger.Api.V2;
using Daml.Ledger.Abstractions;
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
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

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
    public static string ActivitySourceName => typeof(LedgerClient).FullName!;

    private static readonly ActivitySource ActivitySource = new(typeof(LedgerClient).FullName!);
    private static readonly ILogger<LedgerClient> Logger = StaticLoggerFactory.Create<LedgerClient>();

    private readonly GrpcChannel _channel;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly UpdateService.UpdateServiceClient _updateService;
    private readonly StateService.StateServiceClient _stateService;
    private readonly LedgerClientOptions _options;
    private readonly ITokenProvider? _tokenProvider;

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

        _channel = GrpcChannel.ForAddress(_options.GrpcAddress, new GrpcChannelOptions
        {
            MaxReceiveMessageSize = _options.MaxMessageSize,
            MaxSendMessageSize = _options.MaxMessageSize
        });

        _commandService = new CommandService.CommandServiceClient(_channel);
        _updateService = new UpdateService.UpdateServiceClient(_channel);
        _stateService = new StateService.StateServiceClient(_channel);

        LogInitialized(Logger, _options.GrpcAddress);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "LedgerClient initialized with endpoint {Endpoint}")]
    private static partial void LogInitialized(ILogger logger, string endpoint);

    /// <summary>
    /// Creates a new LedgerClient with injected gRPC channel and service client.
    /// This constructor is intended for testing scenarios. Update / state
    /// service clients are constructed from <paramref name="channel"/> when
    /// this overload is used; tests that exercise streaming inject mock
    /// instances via <see cref="LedgerClient(LedgerClientOptions, GrpcChannel, CommandService.CommandServiceClient, UpdateService.UpdateServiceClient, StateService.StateServiceClient, ITokenProvider?)"/>.
    /// </summary>
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

    /// <summary>
    /// Creates a new LedgerClient with all gRPC service clients injected.
    /// This constructor is intended for streaming-test scenarios where the
    /// update and state services need to be substituted alongside the
    /// command service.
    /// </summary>
    internal LedgerClient(
        LedgerClientOptions options,
        GrpcChannel channel,
        CommandService.CommandServiceClient commandService,
        UpdateService.UpdateServiceClient updateService,
        StateService.StateServiceClient stateService,
        ITokenProvider? tokenProvider = null)
    {
        _options = options;
        _channel = channel;
        _commandService = commandService;
        _updateService = updateService;
        _stateService = stateService;
        _tokenProvider = tokenProvider;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Creating contract {TemplateType}")]
    private static partial void LogCreatingContract(ILogger logger, string templateType);

    /// <inheritdoc />
    public Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        RuntimeCommands.ExerciseCommand command,
        string actAs,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
        => TryExerciseAsync<TResult>(command, (RuntimeCommands.SubmitterInfo)actAs, workflowId, cancellationToken);

    /// <inheritdoc />
    public async Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        RuntimeCommands.ExerciseCommand command,
        RuntimeCommands.SubmitterInfo submitter,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivityHelper.StartActivity<LedgerClient>(ActivitySource);
        activity?.SetTag(LedgerClientActivityTags.Choice, command.Choice);
        activity?.SetTag(LedgerClientActivityTags.ContractId, command.ContractId);
        SetSubmitterTags(activity, submitter);

        var submission = RuntimeCommands.CommandsSubmission.Single(command)
            .WithSubmitter(submitter)
            .WithCommandId(Guid.NewGuid().ToString())
            .WithWorkflowId(workflowId ?? $"exercise-{command.Choice.ToLowerInvariant()}");

        LogExercisingChoice(Logger, command.Choice, command.ContractId);

        var commands = BuildCommands(submission);

        var request = new SubmitAndWaitForTransactionRequest
        {
            Commands = commands,
            TransactionFormat = new TransactionFormat
            {
                TransactionShape = TransactionShape.LedgerEffects,
                EventFormat = new EventFormat { Verbose = true }
            }
        };

        try
        {
            var response = await _commandService.SubmitAndWaitForTransactionAsync(
                request,
                headers: await GetHeadersAsync(cancellationToken),
                deadline: GetDeadline(),
                cancellationToken: cancellationToken);

            var transaction = response.Transaction
                ?? throw new InvalidOperationException(
                    "Server returned a successful response but no Transaction was present.");

            var exercisedEvent = transaction.Events
                .Where(e => e.EventCase == Event.EventOneofCase.Exercised)
                .Select(e => e.Exercised)
                .FirstOrDefault(e => e.ContractId == command.ContractId && e.Choice == command.Choice)
                ?? throw new InvalidOperationException(
                    $"No ExercisedEvent found for choice {command.Choice} on {command.ContractId}");

            LogChoiceExercised(Logger, command.Choice, command.ContractId);

            var exerciseResult = exercisedEvent.ExerciseResult
                ?? throw new InvalidOperationException(
                    $"ExercisedEvent for choice {command.Choice} on {command.ContractId} has no ExerciseResult.");

            var resultValue = DamlValueConverter.FromProtoValue(exerciseResult);
            var result = resultValue.FromDamlValue<TResult>()!;
            return new ExerciseOutcome<TResult>.One(result);
        }
        catch (RpcException ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogChoiceExerciseFailed(Logger, command.Choice, command.ContractId, ex.StatusCode, ex.Status.Detail);
            var (category, errorId, message, metadata) = DamlErrorParser.Parse(ex);
            if (errorId.Length > 0)
                return new ExerciseOutcome<TResult>.DamlError(category, errorId, message, metadata);

            return new ExerciseOutcome<TResult>.InfraError((int)ex.StatusCode, ex.Status.Detail ?? ex.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Exercising choice {Choice} on {ContractId}")]
    private static partial void LogExercisingChoice(ILogger logger, string choice, string contractId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Choice exercised: {Choice} on {ContractId}")]
    private static partial void LogChoiceExercised(ILogger logger, string choice, string contractId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to exercise choice {Choice} on {ContractId}: {StatusCode} — {Detail}")]
    private static partial void LogChoiceExerciseFailed(ILogger logger, string choice, string contractId, StatusCode statusCode, string? detail);

    /// <inheritdoc />
    public async Task<string> SubmitAsync(
        RuntimeCommands.CommandsSubmission submission,
        CancellationToken cancellationToken = default)
    {
        var result = await SubmitAndWaitAsync(submission, cancellationToken);
        return result.UpdateId;
    }

    /// <inheritdoc />
    public async Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
        RuntimeCommands.CommandsSubmission submission,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivityHelper.StartActivity<LedgerClient>(ActivitySource);

        var commands = BuildCommands(submission);
        var request = new SubmitAndWaitForTransactionRequest { Commands = commands };

        LogSubmittingCommands(Logger, submission.Commands.Count);

        try
        {
            var response = await _commandService.SubmitAndWaitForTransactionAsync(
                request,
                headers: await GetHeadersAsync(cancellationToken),
                deadline: GetDeadline(),
                cancellationToken: cancellationToken);

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
            // Distinguish structured Daml errors (rich error model) from infra failures.
            // If the trailer carries an ErrorInfo we treat it as a Daml error, even on
            // status codes that aren't intrinsically Canton (server choice).
            var (category, errorId, message, metadata) = DamlErrorParser.Parse(ex);
            if (errorId.Length > 0)
            {
                return new ExerciseOutcome<TransactionResult>.DamlError(category, errorId, message, metadata);
            }

            return new ExerciseOutcome<TransactionResult>.InfraError((int)ex.StatusCode, ex.Status.Detail ?? ex.Message);
        }
    }

    /// <inheritdoc />
    public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload,
        string actAs,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate
        => TryCreateAsync(payload, (RuntimeCommands.SubmitterInfo)actAs, workflowId, cancellationToken);

    /// <inheritdoc />
    public async Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload,
        RuntimeCommands.SubmitterInfo submitter,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate
    {
        using var activity = ActivityHelper.StartActivity<LedgerClient>(ActivitySource);
        activity?.SetTag(LedgerClientActivityTags.TemplateType, typeof(TTemplate).Name);
        SetSubmitterTags(activity, submitter);

        var createCommand = RuntimeCommands.CreateCommand.For(payload);
        var submission = RuntimeCommands.CommandsSubmission.Single(createCommand)
            .WithSubmitter(submitter)
            .WithCommandId(Guid.NewGuid().ToString())
            .WithWorkflowId(workflowId ?? $"create-{typeof(TTemplate).Name.ToLowerInvariant()}");

        LogCreatingContract(Logger, typeof(TTemplate).Name);

        var outcome = await TrySubmitAndWaitForTransactionAsync(submission, cancellationToken);
        return TransactionResultProjector.ProjectToContractId<TTemplate>(outcome);
    }

    /// <inheritdoc />
    public Task<ExerciseOutcome<ContractId<TTemplate>>> TryExerciseForCreatedAsync<TTemplate>(
        RuntimeCommands.ExerciseCommand command,
        string actAs,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate
        => TryExerciseForCreatedAsync<TTemplate>(command, (RuntimeCommands.SubmitterInfo)actAs, workflowId, cancellationToken);

    /// <inheritdoc />
    public async Task<ExerciseOutcome<ContractId<TTemplate>>> TryExerciseForCreatedAsync<TTemplate>(
        RuntimeCommands.ExerciseCommand command,
        RuntimeCommands.SubmitterInfo submitter,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate
    {
        using var activity = ActivityHelper.StartActivity<LedgerClient>(ActivitySource);
        activity?.SetTag(LedgerClientActivityTags.Choice, command.Choice);
        activity?.SetTag(LedgerClientActivityTags.ContractId, command.ContractId);
        activity?.SetTag(LedgerClientActivityTags.TemplateType, typeof(TTemplate).Name);
        SetSubmitterTags(activity, submitter);

        var submission = RuntimeCommands.CommandsSubmission.Single(command)
            .WithSubmitter(submitter)
            .WithCommandId(Guid.NewGuid().ToString())
            .WithWorkflowId(workflowId ?? $"exercise-{command.Choice.ToLowerInvariant()}");

        LogExercisingChoice(Logger, command.Choice, command.ContractId);

        var outcome = await TrySubmitAndWaitForTransactionAsync(submission, cancellationToken);
        return TransactionResultProjector.ProjectToContractId<TTemplate>(outcome);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Submitting {CommandCount} commands")]
    private static partial void LogSubmittingCommands(ILogger logger, int commandCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transaction completed: {UpdateId}, Created: {CreatedCount}, Archived: {ArchivedCount}")]
    private static partial void LogTransactionCompleted(ILogger logger, string updateId, int createdCount, int archivedCount);

    private async Task<SubmitAndWaitResponse> SubmitAndWaitAsync(
        RuntimeCommands.CommandsSubmission submission,
        CancellationToken cancellationToken)
    {
        var commands = BuildCommands(submission);
        var request = new SubmitAndWaitRequest { Commands = commands };

        return await _commandService.SubmitAndWaitAsync(
            request,
            headers: await GetHeadersAsync(cancellationToken),
            deadline: GetDeadline(),
            cancellationToken: cancellationToken);
    }

    internal Commands BuildCommands(RuntimeCommands.CommandsSubmission submission)
    {
        var commands = new Commands
        {
            CommandId = submission.CommandId ?? Guid.NewGuid().ToString(),
            WorkflowId = submission.WorkflowId ?? string.Empty,
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
                        ContractId = exercise.ContractId,
                        Choice = exercise.Choice,
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

    /// <inheritdoc />
    public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        string actAs,
        long? fromOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actAs);
        var templateFilter = DamlValueConverter.ToProtoTemplateNameIdentifier(T.PackageName, T.TemplateId);
        return SubscribeAsyncCore<T>((RuntimeCommands.SubmitterInfo)actAs, templateFilter, fromOffset, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        RuntimeCommands.SubmitterInfo submitter,
        long? fromOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        var templateFilter = DamlValueConverter.ToProtoTemplateNameIdentifier(T.PackageName, T.TemplateId);
        return SubscribeAsyncCore<T>(submitter, templateFilter, fromOffset, cancellationToken);
    }

    private async IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsyncCore<T>(
        RuntimeCommands.SubmitterInfo submitter,
        ProtoIdentifier templateFilter,
        long? fromOffset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : ITemplate
    {
        using var activity = ActivityHelper.StartActivity<LedgerClient>(ActivitySource);
        activity?.SetTag(LedgerClientActivityTags.TemplateType, typeof(T).Name);
        activity?.SetTag(LedgerClientActivityTags.FromOffset, fromOffset);
        SetSubmitterTags(activity, submitter);

        var templateId = T.TemplateId;
        var request = SubscribeRequestBuilder.BuildGetUpdatesRequest(
            submitter,
            templateFilter,
            fromOffset);

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
                yield return new ContractStreamEvent<T>.StreamError(
                    (int)fault.StatusCode,
                    fault.Status.Detail ?? fault.Message);
                yield break;
            }

            if (!step.Moved) yield break;

            foreach (var typedEvent in ProjectUpdate<T>(stream.Current, templateId))
            {
                yield return typedEvent;
            }
        }
    }

    private static IEnumerable<ContractStreamEvent<T>> ProjectUpdate<T>(
        GetUpdatesResponse response,
        RuntimeIdentifier templateId)
        where T : ITemplate
    {
        switch (response.UpdateCase)
        {
            case GetUpdatesResponse.UpdateOneofCase.Transaction:
                foreach (var typedEvent in ContractStreamProjector.ProjectTransactionEvents<T>(response.Transaction, templateId))
                {
                    yield return typedEvent;
                }
                break;
            case GetUpdatesResponse.UpdateOneofCase.OffsetCheckpoint:
                yield return new ContractStreamEvent<T>.Checkpoint(response.OffsetCheckpoint.Offset);
                break;
            case GetUpdatesResponse.UpdateOneofCase.Reassignment:
                foreach (var typedEvent in ContractStreamProjector.ProjectReassignmentEvents<T>(response.Reassignment, templateId))
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
    public IAsyncEnumerable<ContractStreamEvent<T>.Created> SubscribeActiveAsync<T>(
        string actAs,
        CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actAs);
        var templateFilter = DamlValueConverter.ToProtoTemplateNameIdentifier(T.PackageName, T.TemplateId);
        return SubscribeActiveAsyncCore<T>((RuntimeCommands.SubmitterInfo)actAs, templateFilter, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ContractStreamEvent<T>.Created> SubscribeActiveAsync<T>(
        RuntimeCommands.SubmitterInfo submitter,
        CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        var templateFilter = DamlValueConverter.ToProtoTemplateNameIdentifier(T.PackageName, T.TemplateId);
        return SubscribeActiveAsyncCore<T>(submitter, templateFilter, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> GetLedgerEndAsync(CancellationToken cancellationToken = default)
    {
        using var activity = ActivityHelper.StartActivity<LedgerClient>(ActivitySource);
        var response = await _stateService.GetLedgerEndAsync(
            new GetLedgerEndRequest(),
            headers: await GetHeadersAsync(cancellationToken),
            deadline: GetDeadline(),
            cancellationToken: cancellationToken);
        activity?.SetTag(LedgerClientActivityTags.Offset, response.Offset);
        return response.Offset;
    }

    private async IAsyncEnumerable<ContractStreamEvent<T>.Created> SubscribeActiveAsyncCore<T>(
        RuntimeCommands.SubmitterInfo submitter,
        ProtoIdentifier templateFilter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : ITemplate
    {
        using var activity = ActivityHelper.StartActivity<LedgerClient>(ActivitySource);
        activity?.SetTag(LedgerClientActivityTags.TemplateType, typeof(T).Name);
        SetSubmitterTags(activity, submitter);

        var templateId = T.TemplateId;

        var sharedHeaders = await GetHeadersAsync(cancellationToken);
        var ledgerEnd = await _stateService.GetLedgerEndAsync(
            new GetLedgerEndRequest(),
            headers: sharedHeaders,
            deadline: GetDeadline(),
            cancellationToken: cancellationToken);

        var request = SubscribeRequestBuilder.BuildGetActiveContractsRequest(
            submitter,
            templateFilter,
            ledgerEnd.Offset);

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
                RethrowActiveContractsStreamFault<T>(fault);
            }

            if (!step.Moved) yield break;

            var created = ContractStreamProjector.ExtractCreatedEvent(stream.Current);
            if (created is null) continue;
            if (!ContractStreamProjector.IsTemplateMatch(created.TemplateId, templateId)) continue;

            yield return ContractStreamProjector.CreatedFromProto<T>(created);
        }
    }

    private static void SetSubmitterTags(Activity? activity, RuntimeCommands.SubmitterInfo submitter)
    {
        if (activity is null) return;
        activity.SetTag(LedgerClientActivityTags.SubmitterActAs, string.Join(",", submitter.ActAs.Select(p => p.Id)));
        if (submitter.ReadAs.Count > 0)
        {
            activity.SetTag(LedgerClientActivityTags.SubmitterReadAs, string.Join(",", submitter.ReadAs.Select(p => p.Id)));
        }
    }

    private static void RethrowActiveContractsStreamFault<T>(RpcException fault)
        where T : ITemplate
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

    /// <summary>
    /// Releases the underlying gRPC channel.
    /// </summary>
    public void Dispose()
    {
        _channel?.Dispose();
    }
}
