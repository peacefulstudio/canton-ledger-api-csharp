// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Globalization;
using Canton.Ledger.Abstractions;
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
using Microsoft.Extensions.Logging;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client;

internal sealed partial class SubmissionClient
{
    private const string DuplicateCommandErrorId = "DUPLICATE_COMMAND";
    private const string CompletionOffsetMetadataKey = "completion_offset";

    private readonly LedgerCallInvoker _invoker;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly CommandSubmissionService.CommandSubmissionServiceClient _commandSubmissionService;
    private readonly LedgerClientOptions _options;
    private readonly ILogger _logger;
    private readonly Func<long, RuntimeCommands.SubmitterInfo, CancellationToken, Task<TransactionResult>> _pointReadByOffset;
    private readonly Func<long, RuntimeCommands.SubmitterInfo, CancellationToken, Task<TransactionTree>> _treePointReadByOffset;
    private readonly CommandBuilder _commandBuilder;

    internal SubmissionClient(
        LedgerCallInvoker invoker,
        CommandService.CommandServiceClient commandService,
        CommandSubmissionService.CommandSubmissionServiceClient commandSubmissionService,
        CommandBuilder commandBuilder,
        LedgerClientOptions options,
        ILogger logger,
        Func<long, RuntimeCommands.SubmitterInfo, CancellationToken, Task<TransactionResult>> pointReadByOffset,
        Func<long, RuntimeCommands.SubmitterInfo, CancellationToken, Task<TransactionTree>> treePointReadByOffset)
    {
        _invoker = invoker;
        _commandService = commandService;
        _commandSubmissionService = commandSubmissionService;
        _commandBuilder = commandBuilder;
        _options = options;
        _logger = logger;
        _pointReadByOffset = pointReadByOffset;
        _treePointReadByOffset = treePointReadByOffset;
    }

    internal Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        RuntimeCommands.ExerciseCommand command,
        RuntimeCommands.SubmitterInfo submitter,
        string? workflowId = null,
        RuntimeCommands.CommandId? commandId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        _invoker.ExecuteOutcomeTracedAsync<SubmissionClient, ExerciseOutcome<TResult>>(
            LedgerCallInvoker.Source,
            new ServerCall(CommandService.Descriptor, "SubmitAndWaitForTransaction"),
            async (activity, token) =>
            {
                var submission = NewExerciseSubmission(activity, command, submitter, workflowId, commandId);

                var transactionFormat = SubscribeRequestBuilder.BuildTransactionFormat(submitter);

                var commands = _commandBuilder.BuildCommands(submission);
                var outcome = await TrySubmitCoreAsync(
                    commands, transactionFormat, submitter, GrpcTransactionResultProjector.Project, _pointReadByOffset,
                    timeout, token).ConfigureAwait(false);

                LogChoiceExerciseOutcome(outcome, command);

                return GrpcTransactionResultProjector.ProjectChoiceResult<TResult>(outcome, command.Choice);
            },
            RecordOutcome,
            cancellationToken);

    internal Task<SubmitAndWaitResult> SubmitAndWaitAsync(
        RuntimeCommands.CommandsSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        _invoker.ExecuteTracedAsync<SubmissionClient, SubmitAndWaitResult>(
            LedgerCallInvoker.Source,
            CommandService.Descriptor,
            "SubmitAndWait",
            async (_, token) =>
            {
                var commands = _commandBuilder.BuildCommands(submission);
                var response = await SubmitAndWaitCoreAsync(commands, timeout, token).ConfigureAwait(false);
                return new SubmitAndWaitResult(
                    (RuntimeCommands.CommandId)commands.CommandId,
                    response.UpdateId,
                    LedgerOffset.At(response.CompletionOffset));
            },
            cancellationToken);

    internal Task<RuntimeCommands.CommandId> SubmitAsync(
        RuntimeCommands.CommandsSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        _invoker.ExecuteTracedAsync<SubmissionClient, RuntimeCommands.CommandId>(
            LedgerCallInvoker.Source,
            CommandSubmissionService.Descriptor,
            "Submit",
            async (_, token) =>
            {
                var commands = _commandBuilder.BuildCommands(submission);
                LogFireSubmit(_logger, commands.CommandId, submission.Commands.Count);

                var request = new SubmitRequest { Commands = commands };
                await _invoker.InvokeAsync(
                    (headers, deadline, callToken) =>
                        _commandSubmissionService.SubmitAsync(request, headers, deadline, callToken),
                    token,
                    timeout).ConfigureAwait(false);

                return (RuntimeCommands.CommandId)commands.CommandId;
            },
            cancellationToken);

    internal Task<RuntimeCommands.CommandId> SubmitReassignmentAsync(
        ReassignmentSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        _invoker.ExecuteTracedAsync<SubmissionClient, RuntimeCommands.CommandId>(
            LedgerCallInvoker.Source,
            CommandSubmissionService.Descriptor,
            "SubmitReassignment",
            async (_, token) =>
            {
                var commands = _commandBuilder.BuildReassignmentCommands(submission);
                LogFireReassignment(_logger, commands.CommandId, submission.Command.GetType().Name);

                var request = new SubmitReassignmentRequest { ReassignmentCommands = commands };
                await _invoker.InvokeAsync(
                    (headers, deadline, callToken) =>
                        _commandSubmissionService.SubmitReassignmentAsync(request, headers, deadline, callToken),
                    token,
                    timeout).ConfigureAwait(false);

                return (RuntimeCommands.CommandId)commands.CommandId;
            },
            cancellationToken);

    internal Task<ExerciseOutcome<ContractStreamEvent<T>>> TrySubmitAndWaitForReassignmentAsync<T>(
        ReassignmentSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T> =>
        _invoker.ExecuteOutcomeTracedAsync<SubmissionClient, ExerciseOutcome<ContractStreamEvent<T>>>(
            LedgerCallInvoker.Source,
            new ServerCall(CommandService.Descriptor, "SubmitAndWaitForReassignment"),
            async (_, token) =>
            {
                var commands = _commandBuilder.BuildReassignmentCommands(submission);
                LogAwaitReassignment(_logger, commands.CommandId, submission.Command.GetType().Name);

                var submitter = new RuntimeCommands.SubmitterInfo(
                    new HashSet<Daml.Runtime.Data.Party> { submission.Submitter }, new HashSet<Daml.Runtime.Data.Party>());
                var eventFormat = SubscribeRequestBuilder.BuildReassignmentEventFormat(
                    submitter, MarkerMatcher<T>.StreamFilterIdentifier(), MarkerMatcher<T>.IsInterface);

                var request = new SubmitAndWaitForReassignmentRequest
                {
                    ReassignmentCommands = commands,
                    EventFormat = eventFormat,
                };

                try
                {
                    var response = await _invoker.InvokeAsync(
                        (headers, deadline, callToken) =>
                            _commandService.SubmitAndWaitForReassignmentAsync(request, headers, deadline, callToken),
                        token,
                        timeout).ConfigureAwait(false);

                    ContractStreamEvent<T> projected;
                    try
                    {
                        projected = ProjectReassignmentResult<T>(response);
                    }
                    catch (Exception decodeFailure) when (decodeFailure is not OperationCanceledException)
                    {
                        LogReassignmentResponseUndecodable(_logger, decodeFailure);
                        return new ExerciseOutcome<ContractStreamEvent<T>>.InfraError(
                            (int)StatusCode.Internal,
                            $"Could not decode the reassignment in the ledger response: {decodeFailure.Message}",
                            SourceException: decodeFailure);
                    }

                    return new ExerciseOutcome<ContractStreamEvent<T>>.One(projected);
                }
                catch (RpcException ex) when (CallerCancellation.Signals(ex, token))
                {
                    throw CallerCancellation.AsOperationCanceled(ex, token);
                }
                catch (RpcException ex)
                {
                    LogSubmitFailed(_logger, ex.StatusCode, ex.Status.Detail);
                    return ToFailureOutcome<ContractStreamEvent<T>>(ex);
                }
            },
            RecordOutcome,
            cancellationToken);

    private static ContractStreamEvent<T> ProjectReassignmentResult<T>(SubmitAndWaitForReassignmentResponse response)
        where T : ITemplate, IDamlRecord<T>
    {
        var projected = ContractStreamProjector.ProjectReassignmentEvents<T>(response.Reassignment).ToList();
        return projected.FirstOrDefault(e => e is ContractStreamEvent<T>.Assigned or ContractStreamEvent<T>.Unassigned)
            ?? projected.FirstOrDefault()
            ?? new ContractStreamEvent<T>.Unclassified(
                LedgerOffset.At(response.Reassignment.Offset), UnclassifiedKind.EmptyReassignment);
    }

    internal Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
        RuntimeCommands.CommandsSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        _invoker.ExecuteOutcomeTracedAsync<SubmissionClient, ExerciseOutcome<TransactionResult>>(
            LedgerCallInvoker.Source,
            new ServerCall(CommandService.Descriptor, "SubmitAndWaitForTransaction"),
            (_, token) =>
            {
                LogSubmittingCommands(_logger, submission.Commands.Count);

                var commands = _commandBuilder.BuildCommands(submission);
                return TrySubmitCoreAsync(
                    commands, transactionFormat: null, SubmitterFrom(submission), GrpcTransactionResultProjector.Project,
                    _pointReadByOffset, timeout, token);
            },
            RecordOutcome,
            cancellationToken);

    internal Task<ExerciseOutcome<TransactionTree>> TrySubmitAndWaitForTransactionTreeAsync(
        RuntimeCommands.CommandsSubmission submission,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        _invoker.ExecuteOutcomeTracedAsync<SubmissionClient, ExerciseOutcome<TransactionTree>>(
            LedgerCallInvoker.Source,
            new ServerCall(CommandService.Descriptor, "SubmitAndWaitForTransaction"),
            (_, token) =>
            {
                LogSubmittingCommands(_logger, submission.Commands.Count);

                var commands = _commandBuilder.BuildCommands(submission.WithSubmitter(submitter));
                return TrySubmitCoreAsync(
                    commands,
                    SubscribeRequestBuilder.BuildTransactionFormat(submitter),
                    submitter,
                    GrpcTransactionTreeProjector.Project,
                    _treePointReadByOffset,
                    timeout,
                    token);
            },
            RecordOutcome,
            cancellationToken,
            configureActivity: activity => activity.SetSubmitterTags(submitter));

    internal Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload,
        RuntimeCommands.SubmitterInfo submitter,
        string? workflowId = null,
        RuntimeCommands.CommandId? commandId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate =>
        _invoker.ExecuteOutcomeTracedAsync<SubmissionClient, ExerciseOutcome<ContractId<TTemplate>>>(
            LedgerCallInvoker.Source,
            serverCall: null,
            async (_, token) =>
            {
                var createCommand = RuntimeCommands.CreateCommand.For(payload);
                var submission = NewSubmission(
                    createCommand, submitter, workflowId ?? $"create-{typeof(TTemplate).Name.ToLowerInvariant()}", commandId);

                LogCreatingContract(_logger, typeof(TTemplate).Name);

                var outcome = await TrySubmitAndWaitForTransactionAsync(submission, timeout, token).ConfigureAwait(false);
                return GrpcTransactionResultProjector.ProjectToContractId<TTemplate>(outcome);
            },
            LedgerCallInvoker.RecordNothing,
            cancellationToken,
            configureActivity: activity =>
            {
                activity?.SetTag(LedgerActivityTagNames.DamlTemplateId, typeof(TTemplate).Name);
                activity.SetSubmitterTags(submitter);
            },
            activityKind: ActivityKind.Internal);

    private async Task<ExerciseOutcome<TProjection>> TrySubmitCoreAsync<TProjection>(
        Commands commands,
        TransactionFormat? transactionFormat,
        RuntimeCommands.SubmitterInfo? submitter,
        Func<Transaction, TProjection> project,
        Func<long, RuntimeCommands.SubmitterInfo, CancellationToken, Task<TProjection>> pointReadByOffset,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var request = new SubmitAndWaitForTransactionRequest { Commands = commands };
        if (transactionFormat is not null)
        {
            request.TransactionFormat = transactionFormat;
        }

        var attemptCount = 0;
        try
        {
            var response = await _invoker.InvokeAsync(
                (headers, deadline, token) =>
                {
                    attemptCount++;
                    return _commandService.SubmitAndWaitForTransactionAsync(request, headers, deadline, token);
                },
                cancellationToken,
                timeout).ConfigureAwait(false);

            Transaction transaction;
            TProjection projected;
            try
            {
                transaction = response.Transaction
                    ?? throw new InvalidOperationException(
                        "Server returned a successful response but no Transaction was present.");
                projected = project(transaction);
            }
            catch (Exception decodeFailure) when (decodeFailure is not OperationCanceledException)
            {
                LogTransactionResponseUndecodable(_logger, decodeFailure);
                return new ExerciseOutcome<TProjection>.InfraError(
                    (int)StatusCode.Internal,
                    $"Could not decode the transaction in the ledger response: {decodeFailure.Message}",
                    SourceException: decodeFailure);
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                var (createdCount, archivedCount) = CountCreatedAndArchived(transaction);
                LogTransactionCompleted(_logger, transaction.UpdateId, createdCount, archivedCount);
            }
            return new ExerciseOutcome<TProjection>.One(projected);
        }
        catch (RpcException ex) when (CallerCancellation.Signals(ex, cancellationToken))
        {
            throw CallerCancellation.AsOperationCanceled(ex, cancellationToken);
        }
        catch (RpcException ex)
        {
            LogSubmitFailed(_logger, ex.StatusCode, ex.Status.Detail);
            var outcome = ToFailureOutcome<TProjection>(ex);
            if (attemptCount > 1
                && outcome is ExerciseOutcome<TProjection>.DamlError { ErrorId: DuplicateCommandErrorId } duplicate)
            {
                return await ResolveRetriedDuplicateAsync(
                    commands.CommandId, submitter, duplicate, pointReadByOffset, cancellationToken).ConfigureAwait(false);
            }

            return outcome;
        }
    }

    private static (int CreatedCount, int ArchivedCount) CountCreatedAndArchived(Transaction transaction)
    {
        var createdCount = 0;
        var archivedCount = 0;
        foreach (var evt in transaction.Events)
        {
            if (evt.EventCase == Event.EventOneofCase.Created)
            {
                createdCount++;
            }
            else if (ArchivesItsContract(evt))
            {
                archivedCount++;
            }
        }
        return (createdCount, archivedCount);
    }

    private static bool ArchivesItsContract(Event evt) => evt.EventCase switch
    {
        Event.EventOneofCase.Archived => true,
        Event.EventOneofCase.Exercised => evt.Exercised.Consuming,
        _ => false,
    };

    private void LogChoiceExerciseOutcome(
        ExerciseOutcome<TransactionResult> outcome, RuntimeCommands.ExerciseCommand command)
    {
        if (outcome is ExerciseOutcome<TransactionResult>.One)
        {
            LogChoiceExercised(_logger, command.Choice, command.ContractId);
            return;
        }

        LogChoiceExerciseFailed(_logger, command.Choice, command.ContractId);
    }

    private static ExerciseOutcome<T> ToFailureOutcome<T>(RpcException exception) =>
        DamlErrorParser.Parse(exception) switch
        {
            ParsedLedgerError.Structured structured => new ExerciseOutcome<T>.DamlError(
                structured.Category, structured.ErrorId, structured.Message, structured.Metadata),
            ParsedLedgerError.Unstructured unstructured => new ExerciseOutcome<T>.InfraError(
                (int)exception.StatusCode,
                exception.Status.Detail ?? exception.Message,
                unstructured.Category,
                exception),
            var parsed => throw new InvalidOperationException(
                $"Unhandled parsed ledger error: {parsed.GetType().Name}"),
        };

    private static void RecordOutcome<T>(Activity? activity, ExerciseOutcome<T> outcome)
    {
        switch (outcome)
        {
            case ExerciseOutcome<T>.DamlError damlError:
                activity.RecordDamlError(damlError.ErrorId);
                break;
            case ExerciseOutcome<T>.InfraError infraError:
                activity.RecordInfraError(infraError.StatusCode, infraError.Message);
                break;
            case ExerciseOutcome<T>.One:
            case ExerciseOutcome<T>.None:
            case ExerciseOutcome<T>.Many:
                break;
        }
    }

    private async Task<ExerciseOutcome<TProjection>> ResolveRetriedDuplicateAsync<TProjection>(
        string commandId,
        RuntimeCommands.SubmitterInfo? submitter,
        ExerciseOutcome<TProjection>.DamlError duplicateError,
        Func<long, RuntimeCommands.SubmitterInfo, CancellationToken, Task<TProjection>> pointReadByOffset,
        CancellationToken cancellationToken)
    {
        if (submitter is not { } dedupReader
            || !duplicateError.Metadata.TryGetValue(CompletionOffsetMetadataKey, out var rawCompletionOffset)
            || !long.TryParse(rawCompletionOffset, NumberStyles.Integer, CultureInfo.InvariantCulture, out var completionOffset)
            || completionOffset <= 0)
        {
            LogRetriedDuplicateUnresolved(_logger, commandId);
            return duplicateError;
        }

        try
        {
            var projected = await pointReadByOffset(completionOffset, dedupReader, cancellationToken).ConfigureAwait(false);
            LogRetriedDuplicateResolved(_logger, commandId, completionOffset);
            return new ExerciseOutcome<TProjection>.One(projected);
        }
        catch (RpcException pointReadFailure)
        {
            LogRetriedDuplicatePointReadFailed(_logger, commandId, completionOffset, pointReadFailure.StatusCode);
            return duplicateError;
        }
        catch (Exception unprojectableUpdate) when (unprojectableUpdate is not OperationCanceledException)
        {
            LogRetriedDuplicateNotProjectable(_logger, commandId, completionOffset, unprojectableUpdate.Message, unprojectableUpdate);
            return duplicateError;
        }
    }

    private async Task<SubmitAndWaitResponse> SubmitAndWaitCoreAsync(
        Commands commands,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var request = new SubmitAndWaitRequest { Commands = commands };

        return await _invoker.InvokeAsync(
            (headers, deadline, token) => _commandService.SubmitAndWaitAsync(request, headers, deadline, token),
            cancellationToken,
            timeout).ConfigureAwait(false);
    }

    private static RuntimeCommands.SubmitterInfo? SubmitterFrom(RuntimeCommands.CommandsSubmission submission) =>
        submission.ActAs is { Count: > 0 } actAs
            ? new RuntimeCommands.SubmitterInfo(actAs.ToHashSet(), submission.ReadAs?.ToHashSet())
            : null;

    private static RuntimeCommands.CommandsSubmission NewSubmission(
        RuntimeCommands.ICommand command,
        RuntimeCommands.SubmitterInfo submitter,
        string workflowId,
        RuntimeCommands.CommandId? commandId) =>
        RuntimeCommands.CommandsSubmission.Single(command)
            .WithSubmitter(submitter)
            .WithCommandId(commandId ?? MintedCommandId())
            .WithWorkflowId(new RuntimeCommands.WorkflowId(workflowId));

    private static RuntimeCommands.CommandId MintedCommandId() => new(Guid.NewGuid().ToString());

    private RuntimeCommands.CommandsSubmission NewExerciseSubmission(
        Activity? activity,
        RuntimeCommands.ExerciseCommand command,
        RuntimeCommands.SubmitterInfo submitter,
        string? workflowId,
        RuntimeCommands.CommandId? commandId)
    {
        activity?.SetTag(LedgerActivityTagNames.DamlChoice, command.Choice.Value);
        activity?.SetTag(LedgerActivityTagNames.DamlContractId, command.ContractId.Value);
        activity.SetSubmitterTags(submitter);
        LogExercisingChoice(_logger, command.Choice, command.ContractId);
        return NewSubmission(
            command, submitter, workflowId ?? $"exercise-{command.Choice.Value.ToLowerInvariant()}", commandId);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Creating contract {TemplateType}")]
    private static partial void LogCreatingContract(ILogger logger, string templateType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Exercising choice {Choice} on {ContractId}")]
    private static partial void LogExercisingChoice(ILogger logger, RuntimeCommands.ChoiceName choice, ContractId contractId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Choice exercised: {Choice} on {ContractId}")]
    private static partial void LogChoiceExercised(ILogger logger, RuntimeCommands.ChoiceName choice, ContractId contractId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to exercise choice {Choice} on {ContractId}")]
    private static partial void LogChoiceExerciseFailed(ILogger logger, RuntimeCommands.ChoiceName choice, ContractId contractId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fire submit of {CommandCount} commands (command_id {CommandId})")]
    private static partial void LogFireSubmit(ILogger logger, string commandId, int commandCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Submitting {CommandCount} commands")]
    private static partial void LogSubmittingCommands(ILogger logger, int commandCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fire reassignment submit of {ReassignmentCommandType} (command_id {CommandId})")]
    private static partial void LogFireReassignment(ILogger logger, string commandId, string reassignmentCommandType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Submitting reassignment {ReassignmentCommandType} and awaiting completion (command_id {CommandId})")]
    private static partial void LogAwaitReassignment(ILogger logger, string commandId, string reassignmentCommandType);

    [LoggerMessage(Level = LogLevel.Error, Message = "SubmitAndWaitForReassignment succeeded but the reassignment in the response could not be decoded — surfaced as an InfraError outcome")]
    private static partial void LogReassignmentResponseUndecodable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transaction completed: {UpdateId}, Created: {CreatedCount}, Archived: {ArchivedCount}")]
    private static partial void LogTransactionCompleted(ILogger logger, string updateId, int createdCount, int archivedCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Submit failed: {StatusCode} — {Detail}")]
    private static partial void LogSubmitFailed(ILogger logger, StatusCode statusCode, string? detail);

    [LoggerMessage(Level = LogLevel.Error, Message = "SubmitAndWaitForTransaction succeeded but the transaction in the response could not be decoded — surfaced as an InfraError outcome")]
    private static partial void LogTransactionResponseUndecodable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retried submission {CommandId} was rejected as DUPLICATE_COMMAND because the first attempt already committed; resolved the committed transaction at completion offset {CompletionOffset} and surfaced it as success")]
    private static partial void LogRetriedDuplicateResolved(ILogger logger, string commandId, long completionOffset);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retried submission {CommandId} was rejected as DUPLICATE_COMMAND — the original submission succeeded — but the rejection carried no usable completion_offset to resolve the committed transaction; surfacing the DUPLICATE_COMMAND error")]
    private static partial void LogRetriedDuplicateUnresolved(ILogger logger, string commandId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retried submission {CommandId} was rejected as DUPLICATE_COMMAND — the original submission succeeded — but the point read at completion offset {CompletionOffset} failed with {StatusCode}; surfacing the DUPLICATE_COMMAND error")]
    private static partial void LogRetriedDuplicatePointReadFailed(ILogger logger, string commandId, long completionOffset, StatusCode statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retried submission {CommandId} was rejected as DUPLICATE_COMMAND — the original submission succeeded — but the update at completion offset {CompletionOffset} could not be projected as a transaction ({Detail}); surfacing the DUPLICATE_COMMAND error")]
    private static partial void LogRetriedDuplicateNotProjectable(ILogger logger, string commandId, long completionOffset, string detail, Exception exception);
}
