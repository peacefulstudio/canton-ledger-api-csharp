// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client.Tests;

public class CommandBuilderTests
{
    private static readonly Party Alice = new("party::alice");
    private static readonly RuntimeCommands.CommandId TestCommandId = new("test-cmd");
    private static readonly SynchronizerId Source = new("sync::source");
    private static readonly SynchronizerId Target = new("sync::target");

    private static CommandBuilder Builder(string? userId = "test-user") =>
        new(new LedgerClientOptions { GrpcAddress = "https://localhost:5001", UserId = userId });

    private static RuntimeCommands.CreateCommand Create() =>
        new(new RuntimeIdentifier("pkg", "Module", "Template"), new DamlRecord(null, []));

    private static RuntimeCommands.ExerciseCommand Exercise(string choice = "Archive", string cid = "00contract123") =>
        new(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new ContractId<LedgerClientTests.TestTemplate>(cid),
            new RuntimeCommands.ChoiceName(choice),
            DamlUnit.Instance);

    [Fact]
    public void BuildCommands_sets_command_id_and_workflow_id()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(Create())
            .WithActAs(Alice)
            .WithCommandId(new RuntimeCommands.CommandId("cmd-123"))
            .WithWorkflowId(new RuntimeCommands.WorkflowId("workflow-456"));

        var commands = Builder().BuildCommands(submission);

        commands.CommandId.Should().Be("cmd-123");
        commands.WorkflowId.Should().Be("workflow-456");
        commands.UserId.Should().Be("test-user");
        commands.ActAs.Should().ContainSingle().Which.Should().Be("party::alice");
    }

    [Fact]
    public void BuildCommands_generates_command_id_when_not_provided()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(Create()).WithActAs(Alice);

        var commands = Builder().BuildCommands(submission);

        commands.CommandId.Should().NotBeNullOrEmpty();
        Guid.TryParse(commands.CommandId, out _).Should().BeTrue();
    }

    [Fact]
    public void BuildCommands_adds_create_command()
    {
        var createCommand = new RuntimeCommands.CreateCommand(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new DamlRecord(
                new RuntimeIdentifier("pkg", "Module", "Template"),
                [new DamlField("owner", new DamlParty("party::alice"))]));

        var submission = RuntimeCommands.CommandsSubmission.Single(createCommand)
            .WithActAs(Alice)
            .WithCommandId(TestCommandId);

        var commands = Builder().BuildCommands(submission);

        commands.Commands_.Should().ContainSingle();
        commands.Commands_[0].Create.Should().NotBeNull();
        commands.Commands_[0].Create.TemplateId.ModuleName.Should().Be("Module");
        commands.Commands_[0].Create.TemplateId.EntityName.Should().Be("Template");
    }

    [Fact]
    public void BuildCommands_adds_exercise_command()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(Exercise())
            .WithActAs(Alice)
            .WithCommandId(TestCommandId);

        var commands = Builder().BuildCommands(submission);

        commands.Commands_.Should().ContainSingle();
        commands.Commands_[0].Exercise.Should().NotBeNull();
        commands.Commands_[0].Exercise.ContractId.Should().Be("00contract123");
        commands.Commands_[0].Exercise.Choice.Should().Be("Archive");
    }

    [Fact]
    public void BuildCommands_includes_read_as_parties()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(Create())
            .WithActAs(Alice)
            .WithReadAs((Party)"party::observer1", (Party)"party::observer2")
            .WithCommandId(TestCommandId);

        var commands = Builder().BuildCommands(submission);

        commands.ReadAs.Should().HaveCount(2);
        commands.ReadAs.Should().Contain("party::observer1");
        commands.ReadAs.Should().Contain("party::observer2");
    }

    [Fact]
    public void BuildCommands_pins_synchronizer_id_from_submission()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(Create())
            .WithActAs(Alice)
            .WithCommandId(TestCommandId)
            .WithSynchronizerId(new SynchronizerId("sync::pinned"));

        var commands = Builder().BuildCommands(submission);

        commands.SynchronizerId.Should().Be("sync::pinned");
    }

    [Fact]
    public void BuildCommands_leaves_synchronizer_id_unset_when_submission_has_none()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(Create())
            .WithActAs(Alice)
            .WithCommandId(TestCommandId);

        var commands = Builder().BuildCommands(submission);

        commands.SynchronizerId.Should().BeEmpty();
    }

    [Fact]
    public void BuildReassignmentCommands_maps_an_unassign_with_source_and_target()
    {
        var submission = ReassignmentSubmission
            .Of(new UnassignCommand("00contract", Source, Target), Alice)
            .WithCommandId(new RuntimeCommands.CommandId("cmd-1"));

        var commands = Builder().BuildReassignmentCommands(submission);

        commands.CommandId.Should().Be("cmd-1");
        commands.Submitter.Should().Be("party::alice");
        commands.UserId.Should().Be("test-user");
        var unassign = commands.Commands.Should().ContainSingle().Subject.UnassignCommand;
        unassign.ContractId.Should().Be("00contract");
        unassign.Source.Should().Be("sync::source");
        unassign.Target.Should().Be("sync::target");
    }

    [Fact]
    public void BuildReassignmentCommands_maps_an_assign_with_reassignment_id_source_and_target()
    {
        var submission = ReassignmentSubmission
            .Of(new AssignCommand("reassign-42", Source, Target), Alice)
            .WithCommandId(new RuntimeCommands.CommandId("cmd-2"))
            .WithWorkflowId(new RuntimeCommands.WorkflowId("wf-2"));

        var commands = Builder().BuildReassignmentCommands(submission);

        commands.WorkflowId.Should().Be("wf-2");
        var assign = commands.Commands.Should().ContainSingle().Subject.AssignCommand;
        assign.ReassignmentId.Should().Be("reassign-42");
        assign.Source.Should().Be("sync::source");
        assign.Target.Should().Be("sync::target");
    }

    [Fact]
    public void BuildReassignmentCommands_mints_command_id_and_submission_id_when_omitted()
    {
        var submission = ReassignmentSubmission.Of(
            new UnassignCommand("00contract", Source, Target), Alice);

        var commands = Builder().BuildReassignmentCommands(submission);

        Guid.TryParse(commands.CommandId, out _).Should().BeTrue();
        Guid.TryParse(commands.SubmissionId, out _).Should().BeTrue();
    }

    [Fact]
    public void BuildReassignmentCommands_projects_a_supplied_submission_id_unchanged()
    {
        var submission = ReassignmentSubmission
            .Of(new AssignCommand("reassign-1", Source, Target), Alice)
            .WithSubmissionId("sub-1");

        var commands = Builder().BuildReassignmentCommands(submission);

        commands.SubmissionId.Should().Be("sub-1");
    }

    [Fact]
    public void BuildReassignmentCommands_rejects_an_unassign_with_an_empty_contract_id_naming_the_field()
    {
        var submission = ReassignmentSubmission.Of(
            new UnassignCommand("", Source, Target), Alice);

        var act = () => Builder().BuildReassignmentCommands(submission);

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("unassign contract id");
    }

    [Fact]
    public void BuildReassignmentCommands_rejects_an_assign_with_an_empty_reassignment_id_naming_the_field()
    {
        var submission = ReassignmentSubmission.Of(
            new AssignCommand("", Source, Target), Alice);

        var act = () => Builder().BuildReassignmentCommands(submission);

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("assign reassignment id");
    }

    public static TheoryData<string, IReassignmentCommand> DefaultSynchronizerIdCommands => new()
    {
        { "unassign Source", new UnassignCommand("00contract", default, Target) },
        { "unassign Target", new UnassignCommand("00contract", Source, default) },
        { "assign Source", new AssignCommand("reassign-1", default, Target) },
        { "assign Target", new AssignCommand("reassign-1", Source, default) },
    };

    [Theory]
    [MemberData(nameof(DefaultSynchronizerIdCommands))]
    public void BuildReassignmentCommands_rejects_a_default_SynchronizerId_before_building_the_proto(
        string position, IReassignmentCommand command)
    {
        var submission = ReassignmentSubmission.Of(command, Alice);

        var act = () => Builder().BuildReassignmentCommands(submission);

        act.Should().Throw<InvalidOperationException>(
                $"an uninitialized {position} synchronizer id cannot silently reach the wire")
            .WithMessage("*default (uninitialized) SynchronizerId*");
    }
}
