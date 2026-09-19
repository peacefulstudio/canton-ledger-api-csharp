// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime;
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

    private sealed record TestTemplate : ITemplate, IDamlRecord<TestTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "Template");
        public static string PackageId => "pkg";
        public static string PackageName => "pkg-name";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => new(TemplateId, []);

        public static TestTemplate FromRecord(DamlRecord record) =>
            new();
    }

    private interface ITestInterface : IDamlInterface, IHasView<TestInterfaceView>
    {
        static Identifier IDamlInterface.InterfaceId => InterfaceId;
        public static new Identifier InterfaceId { get; } = new("ipkg", "IModule", "IEntity");
        static string IDamlInterface.PackageId => "ipkg";
        static string IDamlInterface.PackageName => "interface-package";
        static Version IDamlInterface.PackageVersion => new(0, 1, 0);

        static DamlTypeDescriptor IDamlType.DamlTypeId =>
            new(InterfaceId, DamlTypeKind.Interface, "interface-package");
    }

    private sealed record TestInterfaceView : IDamlRecord, IDamlRecord<TestInterfaceView>
    {
        public DamlRecord ToRecord() => DamlRecord.Create();

        public static TestInterfaceView FromRecord(DamlRecord record) => new();
    }

    private static RuntimeCommands.CreateCommand Create() =>
        new(new RuntimeIdentifier("pkg", "Module", "Template"), new DamlRecord(null, []));

    private static RuntimeCommands.ExerciseCommand Exercise(string choice = "Archive", string cid = "00contract123") =>
        new(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new ContractId<TestTemplate>(cid),
            new RuntimeCommands.ChoiceName(choice),
            DamlUnit.Instance);

    private static RuntimeCommands.ExerciseCommand InterfaceExercise(
        string choice = "Transfer", string cid = "00interfacecontract") =>
        RuntimeCommands.ExerciseCommand.ForInterface<ITestInterface>(
            new ContractId<ITestInterface>(cid),
            new RuntimeCommands.ChoiceName(choice),
            DamlRecord.Create(DamlField.Create("newOwner", new DamlParty("party::bob"))));

    private static RuntimeCommands.ExerciseByKeyCommand ExerciseByKey(
        string choice = "Transfer", string owner = "party::alice") =>
        new(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new DamlRecord(null, [new DamlField("owner", new DamlParty(owner))]),
            new RuntimeCommands.ChoiceName(choice),
            new DamlRecord(null, [new DamlField("newOwner", new DamlParty("party::bob"))]));

    private static RuntimeCommands.ExerciseByKeyCommand ExerciseByScalarKey(
        string choice = "Transfer", string steward = "party::steward") =>
        new(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new DamlParty(steward),
            new RuntimeCommands.ChoiceName(choice),
            new DamlRecord(null, [new DamlField("newOwner", new DamlParty("party::bob"))]));

    private static readonly DateTimeOffset LedgerTimeBound =
        new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildCommands_maps_a_relative_min_ledger_time_to_minLedgerTimeRel()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(Create())
            .WithActAs(Alice)
            .WithMinLedgerTime(new RuntimeCommands.MinLedgerTime.Relative(TimeSpan.FromSeconds(5)));

        var commands = RestCommandBuilder.BuildCommands(submission, userId: "test-user");

        commands.MinLedgerTimeRel.Should().Be(
            "5s",
            "a caller's do-not-commit-before bound is dropped on the floor while the field stays unset");
        commands.MinLedgerTimeAbs.Should().BeNull("the two bounds are mutually exclusive on the wire");
    }

    [Fact]
    public void BuildCommands_maps_an_absolute_min_ledger_time_to_minLedgerTimeAbs()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(Create())
            .WithActAs(Alice)
            .WithMinLedgerTime(new RuntimeCommands.MinLedgerTime.Absolute(LedgerTimeBound));

        var commands = RestCommandBuilder.BuildCommands(submission, userId: "test-user");

        commands.MinLedgerTimeAbs.Should().Be(LedgerTimeBound);
        commands.MinLedgerTimeRel.Should().BeNull("the two bounds are mutually exclusive on the wire");
    }

    [Fact]
    public void BuildCommands_writes_a_sub_second_relative_bound_as_a_fractional_protobuf_duration()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(Create())
            .WithActAs(Alice)
            .WithMinLedgerTime(new RuntimeCommands.MinLedgerTime.Relative(TimeSpan.FromMilliseconds(1500)));

        var commands = RestCommandBuilder.BuildCommands(submission, userId: "test-user");

        commands.MinLedgerTimeRel.Should().Be("1.5s");
    }

    [Fact]
    public void BuildCommands_omits_minLedgerTimeRel_from_the_wire_when_the_bound_is_absolute()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(Create())
            .WithActAs(Alice)
            .WithCommandId(TestCommandId)
            .WithMinLedgerTime(new RuntimeCommands.MinLedgerTime.Absolute(LedgerTimeBound));

        var json = JsonSerializer.Serialize(
            RestCommandBuilder.BuildCommands(submission, userId: null), RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.TryGetProperty("minLedgerTimeRel", out _).Should().BeFalse(
            "the unset half of a mutually exclusive pair must never be written at all — the generated "
            + "property is a non-nullable string, so filling it with null relies on the serializer to "
            + "undo an assignment the type says cannot happen");
        document.RootElement.TryGetProperty("minLedgerTimeAbs", out _).Should().BeTrue();
    }

    [Fact]
    public void BuildCommands_omits_minLedgerTimeAbs_from_the_wire_when_the_bound_is_relative()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(Create())
            .WithActAs(Alice)
            .WithCommandId(TestCommandId)
            .WithMinLedgerTime(new RuntimeCommands.MinLedgerTime.Relative(TimeSpan.FromSeconds(5)));

        var json = JsonSerializer.Serialize(
            RestCommandBuilder.BuildCommands(submission, userId: null), RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.TryGetProperty("minLedgerTimeAbs", out _).Should().BeFalse();
        document.RootElement.TryGetProperty("minLedgerTimeRel", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-500)]
    [InlineData(-1500)]
    public void A_negative_relative_bound_is_refused_before_it_can_reach_the_builder(int milliseconds)
    {
        var act = () => new RuntimeCommands.MinLedgerTime.Relative(TimeSpan.FromMilliseconds(milliseconds));

        act.Should().Throw<ArgumentOutOfRangeException>(
            "a do-not-commit-before bound that has already passed is not a bound, so the bound type "
            + "refuses it before RestWireConversions.ToWireDuration is ever reached");
    }

    [Fact]
    public void BuildCommands_leaves_both_min_ledger_time_bounds_unset_when_the_submission_imposes_none()
    {
        var commands = RestCommandBuilder.BuildCommands(
            RuntimeCommands.CommandsSubmission.Single(Create()).WithActAs(Alice), userId: "test-user");

        commands.MinLedgerTimeAbs.Should().BeNull();
        commands.MinLedgerTimeRel.Should().BeNull();
    }

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
    public void BuildCommands_pins_the_interface_id_on_an_interface_exercise_command()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(InterfaceExercise())
            .WithActAs(Alice)
            .WithCommandId(TestCommandId);

        var commands = RestCommandBuilder.BuildCommands(submission, userId: null);

        var wireCommand = commands.Commands1.Should().ContainSingle().Subject;
        wireCommand.ExerciseCommand.Should().NotBeNull();
        wireCommand.ExerciseCommand!.TemplateId.PackageId.Should().Be("ipkg");
        wireCommand.ExerciseCommand.TemplateId.ModuleName.Should().Be("IModule");
        wireCommand.ExerciseCommand.TemplateId.EntityName.Should().Be("IEntity");
        wireCommand.ExerciseCommand.ContractId.Should().Be("00interfacecontract");
        wireCommand.ExerciseCommand.Choice.Should().Be("Transfer");

        var argumentField = wireCommand.ExerciseCommand.ChoiceArgument.Record.Fields
            .Should().ContainSingle().Subject;
        argumentField.Label.Should().Be("newOwner");
        argumentField.Value.Party.Should().Be("party::bob");
    }

    [Fact]
    public void BuildCommands_writes_the_interface_id_as_a_flat_colon_separated_string_on_the_wire()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(InterfaceExercise())
            .WithActAs(Alice)
            .WithCommandId(TestCommandId);

        var json = JsonSerializer.Serialize(
            RestCommandBuilder.BuildCommands(submission, userId: null), RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        var exerciseCommand = document.RootElement.GetProperty("commands")[0].GetProperty("ExerciseCommand");
        exerciseCommand.GetProperty("templateId").GetString().Should().Be("ipkg:IModule:IEntity");
    }

    [Fact]
    public void BuildCommands_adds_an_exercise_by_key_command()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(ExerciseByKey())
            .WithActAs(Alice)
            .WithCommandId(TestCommandId);

        var commands = RestCommandBuilder.BuildCommands(submission, userId: null);

        var wireCommand = commands.Commands1.Should().ContainSingle().Subject;
        wireCommand.ExerciseByKeyCommand.Should().NotBeNull();
        wireCommand.ExerciseByKeyCommand!.TemplateId.PackageId.Should().Be("pkg");
        wireCommand.ExerciseByKeyCommand.TemplateId.ModuleName.Should().Be("Module");
        wireCommand.ExerciseByKeyCommand.TemplateId.EntityName.Should().Be("Template");
        wireCommand.ExerciseByKeyCommand.Choice.Should().Be("Transfer");

        var keyField = wireCommand.ExerciseByKeyCommand.ContractKey.Record.Fields
            .Should().ContainSingle().Subject;
        keyField.Label.Should().Be("owner");
        keyField.Value.Party.Should().Be("party::alice");

        var argumentField = wireCommand.ExerciseByKeyCommand.ChoiceArgument.Record.Fields
            .Should().ContainSingle().Subject;
        argumentField.Label.Should().Be("newOwner");
        argumentField.Value.Party.Should().Be("party::bob");

        wireCommand.ExerciseCommand.Should().BeNull();
    }

    [Fact]
    public void BuildCommands_writes_a_bare_scalar_exercise_by_key_key_as_the_scalar_itself()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(ExerciseByScalarKey())
            .WithActAs(Alice)
            .WithCommandId(TestCommandId);

        var commands = RestCommandBuilder.BuildCommands(submission, userId: null);

        var wireCommand = commands.Commands1.Should().ContainSingle().Subject;
        wireCommand.ExerciseByKeyCommand.Should().NotBeNull();
        wireCommand.ExerciseByKeyCommand!.ContractKey.Party.Should().Be(
            "party::steward",
            "a template keyed on a bare scalar is emitted with a KeyEncoder that hands the naked "
            + "DamlValue over, never a single-field record wrapping it");
        wireCommand.ExerciseByKeyCommand.ContractKey.Record.Should().BeNull();
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

    private static RuntimeCommands.CreateAndExerciseCommand CreateAndExercise(
        string choice = "Transfer", string owner = "party::alice") =>
        new(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new DamlRecord(null, [new DamlField("owner", new DamlParty(owner))]),
            new RuntimeCommands.ChoiceName(choice),
            new DamlRecord(null, [new DamlField("newOwner", new DamlParty("party::bob"))]));

    [Fact]
    public void BuildCommands_adds_a_create_and_exercise_command()
    {
        var submission = RuntimeCommands.CommandsSubmission.Single(CreateAndExercise())
            .WithActAs(Alice)
            .WithCommandId(TestCommandId);

        var commands = RestCommandBuilder.BuildCommands(submission, userId: null);

        var wireCommand = commands.Commands1.Should().ContainSingle().Subject;
        var createAndExerciseCommand = wireCommand.CreateAndExerciseCommand;
        createAndExerciseCommand.Should().NotBeNull();
        createAndExerciseCommand!.TemplateId.PackageId.Should().Be("pkg");
        createAndExerciseCommand.TemplateId.ModuleName.Should().Be("Module");
        createAndExerciseCommand.TemplateId.EntityName.Should().Be("Template");
        createAndExerciseCommand.Choice.Should().Be("Transfer");

        var createArgumentField = createAndExerciseCommand.CreateArguments.Fields
            .Should().ContainSingle().Subject;
        createArgumentField.Label.Should().Be("owner");
        createArgumentField.Value.Party.Should().Be("party::alice");

        var choiceArgumentField = createAndExerciseCommand.ChoiceArgument.Record.Fields
            .Should().ContainSingle().Subject;
        choiceArgumentField.Label.Should().Be("newOwner");
        choiceArgumentField.Value.Party.Should().Be("party::bob");

        wireCommand.CreateCommand.Should().BeNull();
        wireCommand.ExerciseCommand.Should().BeNull();
        wireCommand.ExerciseByKeyCommand.Should().BeNull();
    }
}
