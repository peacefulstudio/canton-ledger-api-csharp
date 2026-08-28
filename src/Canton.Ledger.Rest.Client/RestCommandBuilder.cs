// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using static Canton.Ledger.Kernel.Commands.ReassignmentCommandPolicy;
using RuntimeCommands = Daml.Runtime.Commands;
using WireAssignCommand = Canton.Ledger.Rest.Client.Raw.AssignCommand;
using WireCommand = Canton.Ledger.Rest.Client.Raw.Command;
using WireCommands = Canton.Ledger.Rest.Client.Raw.Commands;
using WireCreateCommand = Canton.Ledger.Rest.Client.Raw.CreateCommand;
using WireDisclosedContract = Canton.Ledger.Rest.Client.Raw.DisclosedContract;
using WireExerciseCommand = Canton.Ledger.Rest.Client.Raw.ExerciseCommand;
using WireIdentifier = Canton.Ledger.Rest.Client.Raw.Identifier;
using WireReassignmentCommand = Canton.Ledger.Rest.Client.Raw.ReassignmentCommand;
using WireReassignmentCommandCommand = Canton.Ledger.Rest.Client.Raw.ReassignmentCommandCommand;
using WireReassignmentCommands = Canton.Ledger.Rest.Client.Raw.ReassignmentCommands;
using WireUnassignCommand = Canton.Ledger.Rest.Client.Raw.UnassignCommand;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Builds the generated wire <see cref="WireCommands"/> shape from a transport-neutral
/// <see cref="RuntimeCommands.CommandsSubmission"/>, mirroring the gRPC transport's
/// <c>CommandBuilder</c>. Only <see cref="RuntimeCommands.CreateCommand"/> and
/// <see cref="RuntimeCommands.ExerciseCommand"/> are supported today, matching the gRPC
/// transport's scope.
/// </summary>
internal static class RestCommandBuilder
{
    public static WireCommands BuildCommands(RuntimeCommands.CommandsSubmission submission, string? userId)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var commands = new WireCommands
        {
            CommandId = submission.CommandId?.Value ?? Guid.NewGuid().ToString(),
            WorkflowId = submission.WorkflowId?.Value ?? string.Empty,
            ActAs = (submission.ActAs ?? []).Select(p => p.Id).ToList(),
            ReadAs = (submission.ReadAs ?? []).Select(p => p.Id).ToList(),
            Commands1 = submission.Commands.Select(ToWireCommand).ToList(),
        };

        if (userId is not null)
        {
            commands.UserId = userId;
        }

        if (submission.SynchronizerId is { } synchronizerId)
        {
            commands.SynchronizerId = synchronizerId.Id;
        }

        if (submission.DisclosedContracts is { Count: > 0 } disclosedContracts)
        {
            commands.DisclosedContracts = [.. disclosedContracts.Select(ToWireDisclosedContract)];
        }

        return commands;
    }

    public static WireReassignmentCommands BuildReassignmentCommands(ReassignmentSubmission submission, string? userId)
    {
        ArgumentNullException.ThrowIfNull(submission);
        RequireNonEmpty(submission.Submitter.Id, "submitter");

        var commands = new WireReassignmentCommands
        {
            CommandId = submission.CommandId?.Value ?? Guid.NewGuid().ToString(),
            SubmissionId = submission.SubmissionId ?? Guid.NewGuid().ToString(),
            WorkflowId = submission.WorkflowId?.Value ?? string.Empty,
            Submitter = submission.Submitter.Id,
            Commands = [ToWireReassignmentCommand(submission.Command)],
        };

        if (userId is not null)
        {
            commands.UserId = userId;
        }

        return commands;
    }

    private static WireReassignmentCommand ToWireReassignmentCommand(IReassignmentCommand command) =>
        command switch
        {
            UnassignCommand unassign => new WireReassignmentCommand
            {
                Command = new WireReassignmentCommandCommand
                {
                    UnassignCommand = new WireUnassignCommand
                    {
                        ContractId = RequireNonEmpty(unassign.ContractId, "unassign contract id"),
                        Source = RequireNonEmpty(unassign.Source.Id, "unassign source synchronizer id"),
                        Target = RequireNonEmpty(unassign.Target.Id, "unassign target synchronizer id"),
                    },
                },
            },
            AssignCommand assign => new WireReassignmentCommand
            {
                Command = new WireReassignmentCommandCommand
                {
                    AssignCommand = new WireAssignCommand
                    {
                        ReassignmentId = RequireNonEmpty(assign.ReassignmentId, "assign reassignment id"),
                        Source = RequireNonEmpty(assign.Source.Id, "assign source synchronizer id"),
                        Target = RequireNonEmpty(assign.Target.Id, "assign target synchronizer id"),
                    },
                },
            },
            _ => throw new NotSupportedException(
                $"Reassignment command type {command.GetType().Name} is not supported."),
        };

    private static WireCommand ToWireCommand(RuntimeCommands.ICommand command) =>
        command switch
        {
            RuntimeCommands.CreateCommand create => new WireCommand
            {
                CreateCommand = new WireCreateCommand
                {
                    TemplateId = ToWireIdentifier(create.TemplateId),
                    CreateArguments = RestValueEncoder.ToWireRecord(create.CreateArguments),
                },
            },
            RuntimeCommands.ExerciseCommand exercise => new WireCommand
            {
                ExerciseCommand = new WireExerciseCommand
                {
                    TemplateId = ToWireIdentifier(exercise.TemplateId),
                    ContractId = exercise.ContractId.Value,
                    Choice = exercise.Choice.Value,
                    ChoiceArgument = RestValueEncoder.ToWireValue(exercise.ChoiceArgument),
                },
            },
            _ => throw new NotSupportedException($"Command type {command.GetType().Name} is not supported."),
        };

    private static WireDisclosedContract ToWireDisclosedContract(RuntimeCommands.DisclosedContract disclosed) =>
        new()
        {
            TemplateId = ToWireIdentifier(disclosed.TemplateId),
            ContractId = disclosed.ContractId,
            CreatedEventBlob = Convert.ToBase64String(disclosed.CreatedEventBlob.Span),
        };

    private static WireIdentifier ToWireIdentifier(Daml.Runtime.Data.Identifier identifier) =>
        new()
        {
            PackageId = identifier.PackageId,
            ModuleName = identifier.ModuleName,
            EntityName = identifier.EntityName,
        };
}
