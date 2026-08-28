// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestCommandBuilderTests
{
    private static readonly Party Alice = new("party::alice");
    private static readonly RuntimeCommands.CommandId TestCommandId = new("test-cmd");
    private static readonly RuntimeIdentifier DisclosedTemplateId = new("disclosed-pkg", "Disclosed", "Contract");

    private sealed record TestTemplate : ITemplate
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "Template");
        public static string PackageId => "pkg";
        public static string PackageName => "pkg-name";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => new(TemplateId, []);
    }

    private static RuntimeCommands.CreateCommand Create() =>
        new(new RuntimeIdentifier("pkg", "Module", "Template"), new DamlRecord(null, []));

    private static RuntimeCommands.ExerciseCommand Exercise(string choice = "Archive", string cid = "00contract123") =>
        new(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new ContractId<TestTemplate>(cid),
            new RuntimeCommands.ChoiceName(choice),
            DamlUnit.Instance);

    [Fact]
    public void BuildCommands_sets_command_id_workflow_id_user_id_and_act_as()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(Create())
            .WithActAs(Alice)
            .WithCommandId(new RuntimeCommands.CommandId("cmd-123"))
            .WithWorkflowId(new RuntimeCommands.WorkflowId("workflow-456"));

        var commands = RestCommandBuilder.BuildCommands(submission, userId: "test-user");

        commands.CommandId.Should().Be("cmd-123");
        commands.WorkflowId.Should().Be("workflow-456");
        commands.UserId.Should().Be("test-user");
        commands.ActAs.Should().ContainSingle().Which.Should().Be("party::alice");
    }

    [Fact]
    public void BuildCommands_generates_a_command_id_when_not_provided()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(Create()).WithActAs(Alice);

        var commands = RestCommandBuilder.BuildCommands(submission, userId: null);

        commands.CommandId.Should().NotBeNullOrEmpty();
        Guid.TryParse(commands.CommandId, out _).Should().BeTrue();
        commands.UserId.Should().BeNull();
    }

    [Fact]
    public void BuildCommands_adds_a_create_command()
    {
        var createCommand = new RuntimeCommands.CreateCommand(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new DamlRecord(
                new RuntimeIdentifier("pkg", "Module", "Template"),
                [new DamlField("owner", new DamlParty("party::alice"))]));
        var submission = RuntimeCommands.CommandsSubmission.Single(createCommand)
            .WithActAs(Alice)
            .WithCommandId(TestCommandId);

        var commands = RestCommandBuilder.BuildCommands(submission, userId: null);

        var wireCommand = commands.Commands1.Should().ContainSingle().Subject;
        wireCommand.CreateCommand.Should().NotBeNull();
        wireCommand.CreateCommand!.TemplateId.ModuleName.Should().Be("Module");
        wireCommand.CreateCommand.TemplateId.EntityName.Should().Be("Template");
        wireCommand.CreateCommand.CreateArguments.Fields.Should().ContainSingle()
            .Which.Label.Should().Be("owner");
    }

    [Fact]
    public void BuildCommands_adds_an_exercise_command()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(Exercise())
            .WithActAs(Alice)
            .WithCommandId(TestCommandId);

        var commands = RestCommandBuilder.BuildCommands(submission, userId: null);

        var wireCommand = commands.Commands1.Should().ContainSingle().Subject;
        wireCommand.ExerciseCommand.Should().NotBeNull();
        wireCommand.ExerciseCommand!.ContractId.Should().Be("00contract123");
        wireCommand.ExerciseCommand.Choice.Should().Be("Archive");
    }

    [Fact]
    public void BuildCommands_includes_read_as_parties()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(Create())
            .WithActAs(Alice)
            .WithReadAs((Party)"party::observer1", (Party)"party::observer2")
            .WithCommandId(TestCommandId);

        var commands = RestCommandBuilder.BuildCommands(submission, userId: null);

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

        var commands = RestCommandBuilder.BuildCommands(submission, userId: null);

        commands.SynchronizerId.Should().Be("sync::pinned");
    }

    [Fact]
    public void BuildCommands_leaves_synchronizer_id_unset_when_submission_has_none()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(Create())
            .WithActAs(Alice)
            .WithCommandId(TestCommandId);

        var commands = RestCommandBuilder.BuildCommands(submission, userId: null);

        commands.SynchronizerId.Should().BeNullOrEmpty();
    }

    [Fact]
    public void BuildCommands_maps_disclosed_contracts_onto_the_wire()
    {
        var blob = new byte[] { 0x01, 0x02, 0x03, 0xFA };
        var submission = RuntimeCommands.CommandsSubmission.Single(Create())
            .WithActAs(Alice)
            .WithCommandId(TestCommandId)
            .WithDisclosedContracts(new RuntimeCommands.DisclosedContract(
                "00disclosed", new RuntimeIdentifier("disclosed-pkg", "Disclosed", "Contract"), blob));

        var commands = RestCommandBuilder.BuildCommands(submission, userId: null);

        var disclosed = commands.DisclosedContracts.Should().ContainSingle().Subject;
        disclosed.ContractId.Should().Be("00disclosed");
        disclosed.TemplateId.PackageId.Should().Be("disclosed-pkg");
        disclosed.TemplateId.ModuleName.Should().Be("Disclosed");
        disclosed.TemplateId.EntityName.Should().Be("Contract");
        disclosed.CreatedEventBlob.Should().Be(Convert.ToBase64String(blob));
    }

    [Fact]
    public void BuildCommands_writes_disclosedContracts_as_base64_on_the_wire()
    {
        var blob = new byte[] { 0x01, 0x02, 0x03, 0xFA };
        var submission = RuntimeCommands.CommandsSubmission.Single(Create())
            .WithActAs(Alice)
            .WithCommandId(TestCommandId)
            .WithDisclosedContracts(new RuntimeCommands.DisclosedContract(
                "00disclosed", DisclosedTemplateId, blob));

        var json = JsonSerializer.Serialize(
            RestCommandBuilder.BuildCommands(submission, userId: null), RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        var disclosed = document.RootElement.GetProperty("disclosedContracts")[0];
        disclosed.GetProperty("contractId").GetString().Should().Be("00disclosed");
        disclosed.GetProperty("createdEventBlob").GetString().Should().Be(Convert.ToBase64String(blob));
    }

    [Fact]
    public void BuildCommands_omits_disclosedContracts_when_submission_has_none()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(Create())
            .WithActAs(Alice)
            .WithCommandId(TestCommandId);

        var json = JsonSerializer.Serialize(
            RestCommandBuilder.BuildCommands(submission, userId: null), RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.TryGetProperty("disclosedContracts", out _).Should().BeFalse();
    }

    [Fact]
    public void BuildCommands_omits_disclosedContracts_when_submission_carries_an_empty_collection()
    {
        var submission = new RuntimeCommands.CommandsSubmission(
            [Create()], ActAs: [Alice], CommandId: TestCommandId, DisclosedContracts: []);

        var json = JsonSerializer.Serialize(
            RestCommandBuilder.BuildCommands(submission, userId: null), RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.TryGetProperty("disclosedContracts", out _).Should().BeFalse();
    }

    [Fact]
    public void BuildReassignmentCommands_nests_an_unassign_command_under_command()
    {
        var submission = ReassignmentSubmission.Of(
            new UnassignCommand("00bb1b", new SynchronizerId("source::1220ab"), new SynchronizerId("target::1220cd")),
            Alice);

        var json = JsonSerializer.Serialize(
            RestCommandBuilder.BuildReassignmentCommands(submission, "user-1"),
            RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        var command = document.RootElement.GetProperty("commands")[0];
        command.EnumerateObject().Select(property => property.Name).Should().Equal("command");
        command.GetProperty("command").GetProperty("UnassignCommand").GetProperty("value")
            .GetProperty("contractId").GetString().Should().Be("00bb1b");
    }

    [Fact]
    public void BuildReassignmentCommands_never_writes_a_flat_unassignCommand_sibling()
    {
        var submission = ReassignmentSubmission.Of(
            new UnassignCommand("00bb1b", new SynchronizerId("source::1220ab"), new SynchronizerId("target::1220cd")),
            Alice);

        var json = JsonSerializer.Serialize(
            RestCommandBuilder.BuildReassignmentCommands(submission, "user-1"),
            RestRefitSettings.SerializerOptions);

        json.Should().NotContain("\"unassignCommand\"");
    }

    [Fact]
    public void BuildReassignmentCommands_nests_an_assign_command_under_command()
    {
        var submission = ReassignmentSubmission.Of(
            new AssignCommand("reassign-1", new SynchronizerId("source::1220ab"), new SynchronizerId("target::1220cd")),
            Alice);

        var json = JsonSerializer.Serialize(
            RestCommandBuilder.BuildReassignmentCommands(submission, "user-1"),
            RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        var command = document.RootElement.GetProperty("commands")[0];
        command.EnumerateObject().Select(property => property.Name).Should().Equal("command");
        command.GetProperty("command").GetProperty("AssignCommand").GetProperty("value")
            .GetProperty("reassignmentId").GetString().Should().Be("reassign-1");
    }

    [Fact]
    public void BuildReassignmentCommands_never_writes_a_flat_assignCommand_sibling()
    {
        var submission = ReassignmentSubmission.Of(
            new AssignCommand("reassign-1", new SynchronizerId("source::1220ab"), new SynchronizerId("target::1220cd")),
            Alice);

        var json = JsonSerializer.Serialize(
            RestCommandBuilder.BuildReassignmentCommands(submission, "user-1"),
            RestRefitSettings.SerializerOptions);

        json.Should().NotContain("\"assignCommand\"");
    }

    [Fact]
    public void BuildReassignmentCommands_rejects_an_unassign_with_an_empty_contract_id_naming_the_field()
    {
        var submission = ReassignmentSubmission.Of(
            new UnassignCommand("", new SynchronizerId("source::1220ab"), new SynchronizerId("target::1220cd")),
            Alice);

        var act = () => RestCommandBuilder.BuildReassignmentCommands(submission, "user-1");

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("unassign contract id");
    }

    [Fact]
    public void BuildReassignmentCommands_rejects_an_assign_with_an_empty_reassignment_id_naming_the_field()
    {
        var submission = ReassignmentSubmission.Of(
            new AssignCommand("", new SynchronizerId("source::1220ab"), new SynchronizerId("target::1220cd")),
            Alice);

        var act = () => RestCommandBuilder.BuildReassignmentCommands(submission, "user-1");

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("assign reassignment id");
    }

    [Fact]
    public void BuildCommands_rejects_an_unsupported_command_type()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(
            new RuntimeCommands.ExerciseByKeyCommand(
                new RuntimeIdentifier("pkg", "Module", "Template"),
                new DamlText("key"),
                new RuntimeCommands.ChoiceName("Archive"),
                DamlUnit.Instance))
            .WithActAs(Alice);

        var act = () => RestCommandBuilder.BuildCommands(submission, userId: null);

        act.Should().Throw<NotSupportedException>();
    }
}
