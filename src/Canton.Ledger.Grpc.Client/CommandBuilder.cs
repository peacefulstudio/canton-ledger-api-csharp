// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Grpc;
using Google.Protobuf;
using static Canton.Ledger.Kernel.Commands.ReassignmentCommandPolicy;
using RuntimeCommands = Daml.Runtime.Commands;
using ProtoUnassignCommand = Com.Daml.Ledger.Api.V2.UnassignCommand;
using ProtoAssignCommand = Com.Daml.Ledger.Api.V2.AssignCommand;

namespace Canton.Ledger.Grpc.Client;

internal sealed class CommandBuilder
{
    private readonly LedgerClientOptions _options;

    internal CommandBuilder(LedgerClientOptions options) => _options = options;

    internal Commands BuildCommands(RuntimeCommands.CommandsSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
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

        if (submission.DisclosedContracts is { Count: > 0 } disclosedContracts)
        {
            commands.DisclosedContracts.AddRange(disclosedContracts.Select(ToProtoDisclosedContract));
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

    internal ReassignmentCommands BuildReassignmentCommands(ReassignmentSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        RequireNonEmpty(submission.Submitter.Id, "submitter");

        var reassignmentCommands = new ReassignmentCommands
        {
            CommandId = submission.CommandId?.Value ?? Guid.NewGuid().ToString(),
            SubmissionId = submission.SubmissionId ?? Guid.NewGuid().ToString(),
            WorkflowId = submission.WorkflowId?.Value ?? string.Empty,
            Submitter = submission.Submitter.Id,
        };

        if (_options.UserId is not null)
        {
            reassignmentCommands.UserId = _options.UserId;
        }

        reassignmentCommands.Commands.Add(ToProtoReassignmentCommand(submission.Command));
        return reassignmentCommands;
    }

    private static DisclosedContract ToProtoDisclosedContract(RuntimeCommands.DisclosedContract disclosed) =>
        new()
        {
            TemplateId = DamlValueConverter.ToProtoIdentifier(disclosed.TemplateId),
            ContractId = disclosed.ContractId,
            CreatedEventBlob = ByteString.CopyFrom(disclosed.CreatedEventBlob.Span),
        };

    private static ReassignmentCommand ToProtoReassignmentCommand(IReassignmentCommand command) =>
        command switch
        {
            Canton.Ledger.Abstractions.UnassignCommand unassign => new ReassignmentCommand
            {
                UnassignCommand = new ProtoUnassignCommand
                {
                    ContractId = RequireNonEmpty(unassign.ContractId, "unassign contract id"),
                    Source = RequireNonEmpty(unassign.Source.Id, "unassign source synchronizer id"),
                    Target = RequireNonEmpty(unassign.Target.Id, "unassign target synchronizer id"),
                },
            },
            Canton.Ledger.Abstractions.AssignCommand assign => new ReassignmentCommand
            {
                AssignCommand = new ProtoAssignCommand
                {
                    ReassignmentId = RequireNonEmpty(assign.ReassignmentId, "assign reassignment id"),
                    Source = RequireNonEmpty(assign.Source.Id, "assign source synchronizer id"),
                    Target = RequireNonEmpty(assign.Target.Id, "assign target synchronizer id"),
                },
            },
            _ => throw new NotSupportedException(
                $"Reassignment command type {command.GetType().Name} is not supported"),
        };
}
