// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using System.Diagnostics;
using Canton.Ledger.Auth;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Grpc;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// Implementation of the Canton Ledger API client using gRPC.
/// </summary>
public sealed partial class LedgerClient : ILedgerClient
{
    /// <summary>
    /// The <see cref="ActivitySource"/> name used for OpenTelemetry tracing.
    /// Register with <c>tracing.AddSource(LedgerClient.ActivitySourceName)</c>.
    /// </summary>
    public static string ActivitySourceName => typeof(LedgerClient).FullName!;

    private static readonly ActivitySource ActivitySource = new(typeof(LedgerClient).FullName!);
    private static readonly ILogger<LedgerClient> Logger = LoggerFactory.Create<LedgerClient>();

    private readonly GrpcChannel _channel;
    private readonly CommandService.CommandServiceClient _commandService;
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

        LogInitialized(Logger, _options.GrpcAddress);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "LedgerClient initialized with endpoint {Endpoint}")]
    private static partial void LogInitialized(ILogger logger, string endpoint);

    /// <summary>
    /// Creates a new LedgerClient with injected gRPC channel and service client.
    /// This constructor is intended for testing scenarios.
    /// </summary>
    internal LedgerClient(
        LedgerClientOptions options,
        GrpcChannel channel,
        CommandService.CommandServiceClient commandService,
        ITokenProvider? tokenProvider = null)
    {
        _options = options;
        _channel = channel;
        _commandService = commandService;
        _tokenProvider = tokenProvider;
    }

    /// <inheritdoc />
    public async Task<ContractId<T>> CreateAsync<T>(
        T payload,
        string actAs,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        using var activity = ActivityHelper.StartActivity<LedgerClient>(ActivitySource);
        activity?.SetTag("template.type", typeof(T).Name);
        activity?.SetTag("actAs", actAs);

        var createCommand = RuntimeCommands.CreateCommand.For(payload);
        var submission = RuntimeCommands.CommandsSubmission.Single(createCommand)
            .WithActAs((Party)actAs)
            .WithCommandId(Guid.NewGuid().ToString())
            .WithWorkflowId(workflowId ?? $"create-{typeof(T).Name.ToLowerInvariant()}");

        LogCreatingContract(Logger, typeof(T).Name);

        var result = await SubmitAndWaitForTransactionAsync(submission, cancellationToken);

        var createdContract = result.CreatedContracts.Count > 0
            ? result.CreatedContracts[0]
            : throw new InvalidOperationException("No contract was created");

        LogContractCreated(Logger, createdContract.ContractId);

        return new ContractId<T>(createdContract.ContractId);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Creating contract {TemplateType}")]
    private static partial void LogCreatingContract(ILogger logger, string templateType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Contract created: {ContractId}")]
    private static partial void LogContractCreated(ILogger logger, string contractId);

    /// <inheritdoc />
    public async Task<TResult> ExerciseAsync<TResult>(
        RuntimeCommands.ExerciseCommand command,
        string actAs,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivityHelper.StartActivity<LedgerClient>(ActivitySource);
        activity?.SetTag("choice", command.Choice);
        activity?.SetTag("contractId", command.ContractId);

        var submission = RuntimeCommands.CommandsSubmission.Single(command)
            .WithActAs((Party)actAs)
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
        return DamlValueConverter.FromDamlValue<TResult>(resultValue);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Exercising choice {Choice} on {ContractId}")]
    private static partial void LogExercisingChoice(ILogger logger, string choice, string contractId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Choice exercised: {Choice} on {ContractId}")]
    private static partial void LogChoiceExercised(ILogger logger, string choice, string contractId);

    /// <inheritdoc />
    public async Task ExerciseAsync(
        RuntimeCommands.ExerciseCommand command,
        string actAs,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
    {
        await ExerciseAsync<object>(command, actAs, workflowId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> SubmitAsync(
        RuntimeCommands.CommandsSubmission submission,
        CancellationToken cancellationToken = default)
    {
        var result = await SubmitAndWaitAsync(submission, cancellationToken);
        return result.UpdateId;
    }

    /// <inheritdoc />
    public async Task<TransactionResult> SubmitAndWaitForTransactionAsync(
        RuntimeCommands.CommandsSubmission submission,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivityHelper.StartActivity<LedgerClient>(ActivitySource);

        var commands = BuildCommands(submission);

        var request = new SubmitAndWaitForTransactionRequest { Commands = commands };

        LogSubmittingCommands(Logger, submission.Commands.Count);

        var response = await _commandService.SubmitAndWaitForTransactionAsync(
            request,
            headers: await GetHeadersAsync(cancellationToken),
            deadline: GetDeadline(),
            cancellationToken: cancellationToken);

        var transaction = response.Transaction
            ?? throw new InvalidOperationException(
                "Server returned a successful response but no Transaction was present.");

        var createdContracts = new List<CreatedContract>();
        var archivedContractIds = new List<string>();

        foreach (var evt in transaction.Events)
        {
            if (evt.EventCase == Event.EventOneofCase.Created)
            {
                createdContracts.Add(new CreatedContract(
                    evt.Created.ContractId,
                    evt.Created.TemplateId.ToString(),
                    evt.Created.CreateArguments.ToString()));
            }
            else if (evt.EventCase == Event.EventOneofCase.Archived)
            {
                archivedContractIds.Add(evt.Archived.ContractId);
            }
        }

        LogTransactionCompleted(Logger, transaction.UpdateId, createdContracts.Count, archivedContractIds.Count);

        return new TransactionResult(
            transaction.UpdateId,
            transaction.Offset,
            createdContracts,
            archivedContractIds);
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

    private async Task<Metadata?> GetHeadersAsync(CancellationToken cancellationToken)
    {
        if (_tokenProvider is null || ReferenceEquals(_tokenProvider, ITokenProvider.None))
            return null;

        var token = await _tokenProvider.GetTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                $"Token provider {_tokenProvider.GetType().Name} returned an empty token.");

        return new Metadata
        {
            { "authorization", $"Bearer {token}" }
        };
    }

    private DateTime? GetDeadline()
    {
        if (_options.Timeout == null)
            return null;

        return DateTime.UtcNow.Add(_options.Timeout.Value);
    }

    public void Dispose()
    {
        _channel?.Dispose();
        ActivitySource.Dispose();
    }
}
