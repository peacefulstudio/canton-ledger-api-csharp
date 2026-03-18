// Copyright (c) 2026 Peaceful Studio. All rights reserved.

using System.Diagnostics;
using Com.Daml.Ledger.Api.V2;
using Daml.Codegen.CSharp.Runtime.Contracts;
using Daml.Codegen.CSharp.Runtime.Data;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RuntimeCommands = Daml.Codegen.CSharp.Runtime.Commands;
using RuntimeIdentifier = Daml.Codegen.CSharp.Runtime.Data.Identifier;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// Implementation of the Canton Ledger API client using gRPC.
/// </summary>
public sealed partial class LedgerClient : ILedgerClient
{
    private static readonly ActivitySource ActivitySource = new(typeof(LedgerClient).AssemblyQualifiedName!);
    private static readonly ILogger<LedgerClient> Logger = LoggerFactory.Create<LedgerClient>();

    private readonly GrpcChannel _channel;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly LedgerClientOptions _options;

    /// <summary>
    /// Creates a new LedgerClient with the specified options.
    /// </summary>
    public LedgerClient(IOptions<LedgerClientOptions> options)
        : this(options.Value)
    {
    }

    /// <summary>
    /// Creates a new LedgerClient with the specified options.
    /// </summary>
    public LedgerClient(LedgerClientOptions options)
    {
        _options = options;

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
        CommandService.CommandServiceClient commandService)
    {
        _options = options;
        _channel = channel;
        _commandService = commandService;
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
            .WithActAs(actAs)
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
            .WithActAs(actAs)
            .WithCommandId(Guid.NewGuid().ToString())
            .WithWorkflowId(workflowId ?? $"exercise-{command.Choice.ToLowerInvariant()}");

        LogExercisingChoice(Logger, command.Choice, command.ContractId);

        var result = await SubmitAndWaitForTransactionAsync(submission, cancellationToken);

        LogChoiceExercised(Logger, command.Choice, command.ContractId);

        // TODO: Deserialize the choice result from transaction events
        return default!;
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
            headers: GetHeaders(),
            deadline: GetDeadline(),
            cancellationToken: cancellationToken);

        var transaction = response.Transaction;

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
            headers: GetHeaders(),
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
            commands.ActAs.AddRange(submission.ActAs);
        }

        if (submission.ReadAs is not null)
        {
            commands.ReadAs.AddRange(submission.ReadAs);
        }

        foreach (var cmd in submission.Commands)
        {
            var protoCommand = cmd switch
            {
                RuntimeCommands.CreateCommand create => new Command
                {
                    Create = new CreateCommand
                    {
                        TemplateId = ToProtoIdentifier(create.TemplateId),
                        CreateArguments = ToProtoRecord(create.CreateArguments)
                    }
                },
                RuntimeCommands.ExerciseCommand exercise => new Command
                {
                    Exercise = new ExerciseCommand
                    {
                        TemplateId = ToProtoIdentifier(exercise.TemplateId),
                        ContractId = exercise.ContractId,
                        Choice = exercise.Choice,
                        ChoiceArgument = ToProtoValue(exercise.ChoiceArgument)
                    }
                },
                _ => throw new NotSupportedException($"Command type {cmd.GetType().Name} is not supported")
            };

            commands.Commands_.Add(protoCommand);
        }

        return commands;
    }

    internal static ProtoIdentifier ToProtoIdentifier(RuntimeIdentifier identifier)
    {
        return new ProtoIdentifier
        {
            PackageId = identifier.PackageId,
            ModuleName = identifier.ModuleName,
            EntityName = identifier.EntityName
        };
    }

    internal static Record ToProtoRecord(DamlRecord record)
    {
        var protoRecord = new Record();

        if (record.RecordId is not null)
        {
            protoRecord.RecordId = ToProtoIdentifier(record.RecordId);
        }

        foreach (var field in record.Fields)
        {
            protoRecord.Fields.Add(new RecordField
            {
                Label = field.Label ?? string.Empty,
                Value = ToProtoValue(field.Value)
            });
        }

        return protoRecord;
    }

    internal static Value ToProtoValue(DamlValue value)
    {
        return value switch
        {
            DamlUnit => new Value { Unit = new Google.Protobuf.WellKnownTypes.Empty() },
            DamlBool b => new Value { Bool = b.Value },
            DamlInt64 i => new Value { Int64 = i.Value },
            DamlText t => new Value { Text = t.Value },
            DamlParty p => new Value { Party = p.Value },
            DamlNumeric n => new Value { Numeric = n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            DamlDate d => new Value { Date = d.DaysSinceEpoch },
            DamlTimestamp ts => new Value { Timestamp = ts.MicrosecondsSinceEpoch },
            DamlContractId c => new Value { ContractId = c.Value },
            DamlRecord r => new Value { Record = ToProtoRecord(r) },
            DamlVariant v => new Value
            {
                Variant = new Variant
                {
                    Constructor = v.Constructor,
                    Value = ToProtoValue(v.Value),
                    VariantId = v.VariantId is not null ? ToProtoIdentifier(v.VariantId) : null
                }
            },
            DamlList l => ToProtoListValue(l),
            DamlOptional o => new Value
            {
                Optional = new Optional { Value = o.Value is not null ? ToProtoValue(o.Value) : null }
            },
            DamlTextMap m => ToProtoTextMapValue(m),
            DamlGenMap g => ToProtoGenMapValue(g),
            DamlEnum e => new Value
            {
                Enum = new Com.Daml.Ledger.Api.V2.Enum
                {
                    Constructor = e.Constructor,
                    EnumId = e.EnumId is not null ? ToProtoIdentifier(e.EnumId) : null
                }
            },
            _ => throw new NotSupportedException($"DamlValue type {value.GetType().Name} is not supported")
        };
    }

    private static Value ToProtoListValue(DamlList list)
    {
        var protoList = new List();
        foreach (var item in list.Values)
        {
            protoList.Elements.Add(ToProtoValue(item));
        }
        return new Value { List = protoList };
    }

    private static Value ToProtoTextMapValue(DamlTextMap map)
    {
        var protoMap = new TextMap();
        foreach (var kvp in map.Values)
        {
            protoMap.Entries.Add(new TextMap.Types.Entry
            {
                Key = kvp.Key,
                Value = ToProtoValue(kvp.Value)
            });
        }
        return new Value { TextMap = protoMap };
    }

    private static Value ToProtoGenMapValue(DamlGenMap map)
    {
        var protoMap = new GenMap();
        foreach (var entry in map.Entries)
        {
            protoMap.Entries.Add(new GenMap.Types.Entry
            {
                Key = ToProtoValue(entry.Key),
                Value = ToProtoValue(entry.Value)
            });
        }
        return new Value { GenMap = protoMap };
    }

    private Metadata? GetHeaders()
    {
        if (string.IsNullOrEmpty(_options.AccessToken))
            return null;

        return new Metadata
        {
            { "Authorization", $"Bearer {_options.AccessToken}" }
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
