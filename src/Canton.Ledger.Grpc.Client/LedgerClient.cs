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
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
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
        return resultValue.FromDamlValue<TResult>()!;
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

            return new ExerciseOutcome<TransactionResult>.One(ProjectTransaction(response));
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
    public async Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload,
        string actAs,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate
    {
        using var activity = ActivityHelper.StartActivity<LedgerClient>(ActivitySource);
        activity?.SetTag("template.type", typeof(TTemplate).Name);
        activity?.SetTag("actAs", actAs);

        var createCommand = RuntimeCommands.CreateCommand.For(payload);
        var submission = RuntimeCommands.CommandsSubmission.Single(createCommand)
            .WithActAs((Party)actAs)
            .WithCommandId(Guid.NewGuid().ToString())
            .WithWorkflowId(workflowId ?? $"create-{typeof(TTemplate).Name.ToLowerInvariant()}");

        LogCreatingContract(Logger, typeof(TTemplate).Name);

        var outcome = await TrySubmitAndWaitForTransactionAsync(submission, cancellationToken);
        return ProjectToContractId<TTemplate>(outcome);
    }

    /// <inheritdoc />
    public async Task<ExerciseOutcome<ContractId<TTemplate>>> TryExerciseForCreatedAsync<TTemplate>(
        RuntimeCommands.ExerciseCommand command,
        string actAs,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate
    {
        using var activity = ActivityHelper.StartActivity<LedgerClient>(ActivitySource);
        activity?.SetTag("choice", command.Choice);
        activity?.SetTag("contractId", command.ContractId);
        activity?.SetTag("template.type", typeof(TTemplate).Name);

        var submission = RuntimeCommands.CommandsSubmission.Single(command)
            .WithActAs((Party)actAs)
            .WithCommandId(Guid.NewGuid().ToString())
            .WithWorkflowId(workflowId ?? $"exercise-{command.Choice.ToLowerInvariant()}");

        LogExercisingChoice(Logger, command.Choice, command.ContractId);

        var outcome = await TrySubmitAndWaitForTransactionAsync(submission, cancellationToken);
        return ProjectToContractId<TTemplate>(outcome);
    }

    private static ExerciseOutcome<ContractId<TTemplate>> ProjectToContractId<TTemplate>(
        ExerciseOutcome<TransactionResult> outcome)
        where TTemplate : ITemplate
    {
        return outcome switch
        {
            ExerciseOutcome<TransactionResult>.One success => ProjectSuccess<TTemplate>(success.Result),
            ExerciseOutcome<TransactionResult>.DamlError damlError => new ExerciseOutcome<ContractId<TTemplate>>.DamlError(
                damlError.Category, damlError.ErrorId, damlError.Message, damlError.Metadata),
            ExerciseOutcome<TransactionResult>.InfraError infraError => new ExerciseOutcome<ContractId<TTemplate>>.InfraError(
                infraError.StatusCode, infraError.Message),
            _ => throw new InvalidOperationException($"Unhandled outcome: {outcome.GetType().Name}"),
        };
    }

    private static ExerciseOutcome<ContractId<TTemplate>> ProjectSuccess<TTemplate>(TransactionResult result)
        where TTemplate : ITemplate
    {
        var matches = new List<string>();
        var expected = TTemplate.TemplateId;
        foreach (var c in result.CreatedContracts)
        {
            if (string.Equals(c.TemplateId.ModuleName, expected.ModuleName, StringComparison.Ordinal)
                && string.Equals(c.TemplateId.EntityName, expected.EntityName, StringComparison.Ordinal))
            {
                matches.Add(c.ContractId);
            }
        }

        return matches.Count switch
        {
            0 => new ExerciseOutcome<ContractId<TTemplate>>.None(),
            1 => new ExerciseOutcome<ContractId<TTemplate>>.One(new ContractId<TTemplate>(matches[0])),
            _ => new ExerciseOutcome<ContractId<TTemplate>>.Many(matches.Count, matches),
        };
    }

    private static TransactionResult ProjectTransaction(SubmitAndWaitForTransactionResponse response)
    {
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
                    ToRuntimeIdentifier(evt.Created.TemplateId),
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

    private static RuntimeIdentifier ToRuntimeIdentifier(ProtoIdentifier proto) =>
        new(proto.PackageId, proto.ModuleName, proto.EntityName);

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

    /// <inheritdoc />
    public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        string actAs,
        long? fromOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actAs);
        return SubscribeAsyncCore<T>(actAs, fromOffset, cancellationToken);
    }

    private async IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsyncCore<T>(
        string actAs,
        long? fromOffset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : ITemplate
    {
        using var activity = ActivityHelper.StartActivity<LedgerClient>(ActivitySource);
        activity?.SetTag("template.type", typeof(T).Name);
        activity?.SetTag("actAs", actAs);
        activity?.SetTag("fromOffset", fromOffset);

        var templateId = T.TemplateId;
        var protoTemplateId = DamlValueConverter.ToProtoIdentifier(templateId);

        var filters = new Filters();
        filters.Cumulative.Add(new CumulativeFilter
        {
            TemplateFilter = new TemplateFilter
            {
                TemplateId = protoTemplateId,
            },
        });
        var eventFormat = new EventFormat { Verbose = true };
        eventFormat.FiltersByParty.Add(actAs, filters);

        var request = new GetUpdatesRequest
        {
            BeginExclusive = fromOffset ?? 0L,
            UpdateFormat = new UpdateFormat
            {
                IncludeTransactions = new TransactionFormat
                {
                    EventFormat = eventFormat,
                    TransactionShape = TransactionShape.LedgerEffects,
                },
            },
        };

        LogSubscribeStarted(Logger, typeof(T).Name, fromOffset ?? 0L);

        using var call = _updateService.GetUpdates(
            request,
            headers: await GetHeadersAsync(cancellationToken),
            deadline: null,
            cancellationToken: cancellationToken);

        var stream = call.ResponseStream;

        while (true)
        {
            var step = await TryMoveNextAsync(stream, cancellationToken);
            if (step.Faulted is { } fault)
            {
                LogSubscribeStreamError(Logger, typeof(T).Name, fault.StatusCode, fault.Status.Detail);
                yield return new ContractStreamEvent<T>.StreamError(
                    (int)fault.StatusCode,
                    fault.Status.Detail ?? fault.Message);
                yield break;
            }

            if (!step.Moved)
            {
                yield break;
            }

            var current = stream.Current;
            switch (current.UpdateCase)
            {
                case GetUpdatesResponse.UpdateOneofCase.Transaction:
                    foreach (var typedEvent in ProjectTransactionEvents<T>(current.Transaction, templateId))
                    {
                        yield return typedEvent;
                    }
                    break;
                case GetUpdatesResponse.UpdateOneofCase.OffsetCheckpoint:
                    // Always yield — checkpoints are filter-independent and consumers
                    // need them to advance the resume offset during quiet periods.
                    yield return new ContractStreamEvent<T>.Checkpoint(current.OffsetCheckpoint.Offset);
                    break;
                case GetUpdatesResponse.UpdateOneofCase.Reassignment:
                    foreach (var typedEvent in ProjectReassignmentEvents<T>(current.Reassignment, templateId))
                    {
                        yield return typedEvent;
                    }
                    break;
                default:
                    // TopologyTransaction and any future variants — metadata about
                    // party hosting / participant topology, not T-shaped events.
                    LogStreamVariantSkipped(Logger, typeof(T).Name, current.UpdateCase);
                    break;
            }
        }
    }

    /// <summary>
    /// Wraps <see cref="IAsyncStreamReader{T}.MoveNext"/> so the iterator can
    /// distinguish completion (<c>moved == false</c>), cancellation (rethrown),
    /// and gRPC failure (returned as <c>Faulted</c>) — yielding from a catch
    /// block is illegal, so the fault is captured and yielded by the caller.
    /// </summary>
    private static async Task<MoveNextStep> TryMoveNextAsync<TResponse>(
        IAsyncStreamReader<TResponse> stream,
        CancellationToken cancellationToken)
    {
        try
        {
            var moved = await stream.MoveNext(cancellationToken);
            return new MoveNextStep(moved, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RpcException ex)
        {
            return new MoveNextStep(false, ex);
        }
    }

    private readonly record struct MoveNextStep(bool Moved, RpcException? Faulted);

    /// <inheritdoc />
    public IAsyncEnumerable<ContractStreamEvent<T>.Created> SubscribeActiveAsync<T>(
        string actAs,
        CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actAs);
        return SubscribeActiveAsyncCore<T>(actAs, cancellationToken);
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
        activity?.SetTag("offset", response.Offset);
        return response.Offset;
    }

    private async IAsyncEnumerable<ContractStreamEvent<T>.Created> SubscribeActiveAsyncCore<T>(
        string actAs,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : ITemplate
    {
        using var activity = ActivityHelper.StartActivity<LedgerClient>(ActivitySource);
        activity?.SetTag("template.type", typeof(T).Name);
        activity?.SetTag("actAs", actAs);

        var templateId = T.TemplateId;
        var protoTemplateId = DamlValueConverter.ToProtoIdentifier(templateId);

        // Capture headers once and reuse across both gRPC calls to avoid two
        // token fetches per snapshot.
        var headers = await GetHeadersAsync(cancellationToken);

        // Snapshot at current ledger end. Callers can chain SubscribeAsync from
        // this offset to stay current after the snapshot completes.
        var ledgerEnd = await _stateService.GetLedgerEndAsync(
            new GetLedgerEndRequest(),
            headers: headers,
            deadline: GetDeadline(),
            cancellationToken: cancellationToken);

        var filters = new Filters();
        filters.Cumulative.Add(new CumulativeFilter
        {
            TemplateFilter = new TemplateFilter
            {
                TemplateId = protoTemplateId,
            },
        });
        var eventFormat = new EventFormat { Verbose = true };
        eventFormat.FiltersByParty.Add(actAs, filters);

        var request = new GetActiveContractsRequest
        {
            ActiveAtOffset = ledgerEnd.Offset,
            EventFormat = eventFormat,
        };

        LogSubscribeActiveStarted(Logger, typeof(T).Name, ledgerEnd.Offset);

        using var call = _stateService.GetActiveContracts(
            request,
            headers: headers,
            deadline: null,
            cancellationToken: cancellationToken);

        var stream = call.ResponseStream;

        while (true)
        {
            var step = await TryMoveNextAsync(stream, cancellationToken);
            if (step.Faulted is { } fault)
            {
                // Active-contract snapshots project to Created only; surface
                // mid-stream failures as a thrown RpcException rather than
                // in-band, since the StreamError variant isn't part of this
                // method's public type. Callers needing tolerance can wrap
                // this in try/catch, or use SubscribeAsync directly.
                LogSubscribeStreamError(Logger, typeof(T).Name, fault.StatusCode, fault.Status.Detail);
                throw fault;
            }

            if (!step.Moved)
            {
                yield break;
            }

            var response = stream.Current;
            // Project active contracts AND in-flight reassignment entries — both
            // carry CreatedEvent payloads that belong in the snapshot. Dropping
            // IncompleteAssigned / IncompleteUnassigned silently would yield an
            // ACS view that's missing contracts mid-reassignment in
            // multi-synchronizer deployments.
            ProtoCreatedEvent? created = response.ContractEntryCase switch
            {
                GetActiveContractsResponse.ContractEntryOneofCase.ActiveContract
                    => response.ActiveContract?.CreatedEvent,
                GetActiveContractsResponse.ContractEntryOneofCase.IncompleteUnassigned
                    => response.IncompleteUnassigned?.CreatedEvent,
                GetActiveContractsResponse.ContractEntryOneofCase.IncompleteAssigned
                    => response.IncompleteAssigned?.AssignedEvent?.CreatedEvent,
                _ => null,
            };

            if (created is null)
            {
                continue;
            }

            if (!IsTemplateMatch(created.TemplateId, templateId))
            {
                continue;
            }

            yield return CreatedFromProto<T>(created);
        }
    }

    private static IEnumerable<ContractStreamEvent<T>> ProjectTransactionEvents<T>(
        Transaction transaction,
        RuntimeIdentifier templateId)
        where T : ITemplate
    {
        foreach (var evt in transaction.Events)
        {
            switch (evt.EventCase)
            {
                case Event.EventOneofCase.Created:
                    {
                        var created = evt.Created;
                        if (!IsTemplateMatch(created.TemplateId, templateId))
                            continue;
                        yield return CreatedFromProto<T>(created);
                        break;
                    }
                case Event.EventOneofCase.Archived:
                    {
                        var archived = evt.Archived;
                        if (!IsTemplateMatch(archived.TemplateId, templateId))
                            continue;
                        yield return new ContractStreamEvent<T>.Archived(
                            new ContractId<T>(archived.ContractId),
                            archived.Offset,
                            archived.WitnessParties.ToList());
                        break;
                    }
                case Event.EventOneofCase.Exercised:
                    {
                        var exercised = evt.Exercised;
                        if (!IsTemplateMatch(exercised.TemplateId, templateId))
                            continue;
                        var argument = exercised.ChoiceArgument is null
                            ? DamlUnit.Instance
                            : DamlValueConverter.FromProtoValue(exercised.ChoiceArgument);
                        var result = exercised.ExerciseResult is null
                            ? DamlUnit.Instance
                            : DamlValueConverter.FromProtoValue(exercised.ExerciseResult);
                        yield return new ContractStreamEvent<T>.Exercised(
                            new ContractId<T>(exercised.ContractId),
                            exercised.Choice,
                            argument,
                            result,
                            exercised.Consuming,
                            exercised.Offset,
                            exercised.WitnessParties.ToList());
                        break;
                    }
            }
        }
    }

    private static IEnumerable<ContractStreamEvent<T>> ProjectReassignmentEvents<T>(
        Reassignment reassignment,
        RuntimeIdentifier templateId)
        where T : ITemplate
    {
        foreach (var evt in reassignment.Events)
        {
            switch (evt.EventCase)
            {
                case ReassignmentEvent.EventOneofCase.Assigned:
                    {
                        var assigned = evt.Assigned;
                        var created = assigned.CreatedEvent;
                        if (created is null) continue;
                        if (!IsTemplateMatch(created.TemplateId, templateId))
                            continue;
                        var payload = created.CreateArguments is null
                            ? new DamlRecord(null, [])
                            : DamlValueConverter.FromProtoRecord(created.CreateArguments);
                        yield return new ContractStreamEvent<T>.Assigned(
                            new ContractId<T>(created.ContractId),
                            payload,
                            created.Offset,
                            assigned.Source,
                            assigned.Target,
                            created.WitnessParties.ToList());
                        break;
                    }
                case ReassignmentEvent.EventOneofCase.Unassigned:
                    {
                        var unassigned = evt.Unassigned;
                        if (!IsTemplateMatch(unassigned.TemplateId, templateId))
                            continue;
                        yield return new ContractStreamEvent<T>.Unassigned(
                            new ContractId<T>(unassigned.ContractId),
                            unassigned.Offset,
                            unassigned.Source,
                            unassigned.Target,
                            unassigned.WitnessParties.ToList());
                        break;
                    }
            }
        }
    }

    private static ContractStreamEvent<T>.Created CreatedFromProto<T>(ProtoCreatedEvent created)
        where T : ITemplate
    {
        var payload = created.CreateArguments is null
            ? new DamlRecord(null, [])
            : DamlValueConverter.FromProtoRecord(created.CreateArguments);
        return new ContractStreamEvent<T>.Created(
            new ContractId<T>(created.ContractId),
            payload,
            created.Offset,
            created.WitnessParties.ToList());
    }

    private static bool IsTemplateMatch(ProtoIdentifier? proto, RuntimeIdentifier expected)
    {
        if (proto is null) return false;
        // Match on module + entity to tolerate package upgrades. This mirrors
        // the projection logic in ProjectSuccess<TTemplate>.
        return string.Equals(proto.ModuleName, expected.ModuleName, StringComparison.Ordinal)
            && string.Equals(proto.EntityName, expected.EntityName, StringComparison.Ordinal);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Subscribing to {TemplateType} updates from offset {FromOffset}")]
    private static partial void LogSubscribeStarted(ILogger logger, string templateType, long fromOffset);

    [LoggerMessage(Level = LogLevel.Information, Message = "Subscribing to active {TemplateType} contracts at offset {AtOffset}")]
    private static partial void LogSubscribeActiveStarted(ILogger logger, string templateType, long atOffset);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Subscribe stream failed for {TemplateType}: {StatusCode} {Detail}")]
    private static partial void LogSubscribeStreamError(ILogger logger, string templateType, StatusCode statusCode, string detail);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Subscribe stream for {TemplateType} skipped variant {Variant}")]
    private static partial void LogStreamVariantSkipped(ILogger logger, string templateType, GetUpdatesResponse.UpdateOneofCase variant);

    public void Dispose()
    {
        _channel?.Dispose();
        ActivitySource.Dispose();
    }
}
