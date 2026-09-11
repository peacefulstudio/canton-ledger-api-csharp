// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Rest.Client;
using Daml.Runtime.Data;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Pins that a <see cref="RuntimeCommands.ExerciseByKeyCommand"/> — the command the codegen emits
/// for a keyed template's <c>&lt;Choice&gt;ByKeyCommand(key, argument)</c> builder — reaches the wire
/// as the same by-key arm on both transports, carrying the same template id, key, choice name and
/// choice argument. Each transport encodes the key through its own value encoder, so the assertion
/// is on the denoted key rather than on its bytes. Both key shapes the codegen emits are covered:
/// the record a multi-field key is encoded to, and the naked scalar a single-field key is handed
/// over as. The same parity coverage extends to <see cref="RuntimeCommands.CreateAndExerciseCommand"/>,
/// asserting its create-and-exercise arm, template id, choice name, create arguments and choice
/// argument agree across both transports. Both command builders are <c>internal</c>; this project
/// reaches them through <c>InternalsVisibleTo</c> from each client.
/// </summary>
public class LedgerExerciseByKeyParityTests
{
    private static readonly Party Alice = new("party::alice");

    private static readonly RuntimeIdentifier KeyedTemplateId = new("pkg", "Module", "Keyed");

    private static RuntimeCommands.CommandsSubmission Submission(RuntimeCommands.ICommand command) =>
        RuntimeCommands.CommandsSubmission.Single(command).WithActAs(Alice);

    private static RuntimeCommands.ExerciseByKeyCommand ExerciseByKey(DamlValue key) =>
        new(
            KeyedTemplateId,
            key,
            new RuntimeCommands.ChoiceName("Transfer"),
            new DamlRecord(null, [new DamlField("newOwner", new DamlParty("party::bob"))]));

    private static DamlValue RecordKey() =>
        new DamlRecord(null, [new DamlField("owner", new DamlParty(Alice.Id))]);

    private static DamlValue ScalarKey() => new DamlParty("party::steward");

    private static Com.Daml.Ledger.Api.V2.Commands OverGrpc(RuntimeCommands.CommandsSubmission submission) =>
        new CommandBuilder(new LedgerClientOptions { GrpcAddress = "https://localhost:5001", UserId = "u" })
            .BuildCommands(submission);

    private static Rest.Client.Raw.Commands OverRest(RuntimeCommands.CommandsSubmission submission) =>
        RestCommandBuilder.BuildCommands(submission, userId: "u");

    private static Com.Daml.Ledger.Api.V2.ExerciseByKeyCommand SingleByKeyOverGrpc(
        RuntimeCommands.CommandsSubmission submission) =>
        OverGrpc(submission).Commands_.Should().ContainSingle().Subject.ExerciseByKey;

    private static Rest.Client.Raw.ExerciseByKeyCommand SingleByKeyOverRest(
        RuntimeCommands.CommandsSubmission submission) =>
        OverRest(submission).Commands1.Should().ContainSingle().Subject.ExerciseByKeyCommand;

    [Fact]
    public void An_exercise_by_key_reaches_the_wire_as_the_by_key_arm_on_both_transports()
    {
        var submission = Submission(ExerciseByKey(RecordKey()));

        var overGrpc = OverGrpc(submission).Commands_.Should().ContainSingle().Subject;
        var overRest = OverRest(submission).Commands1.Should().ContainSingle().Subject;

        overGrpc.ExerciseByKey.Should().NotBeNull();
        overGrpc.Exercise.Should().BeNull();
        overGrpc.Create.Should().BeNull();
        overRest.ExerciseByKeyCommand.Should().NotBeNull();
        overRest.ExerciseCommand.Should().BeNull();
        overRest.CreateCommand.Should().BeNull();
    }

    [Fact]
    public void An_exercise_by_key_carries_the_same_template_id_and_choice_name_on_both_transports()
    {
        var submission = Submission(ExerciseByKey(RecordKey()));

        var overGrpc = SingleByKeyOverGrpc(submission);
        var overRest = SingleByKeyOverRest(submission);

        overGrpc.TemplateId.PackageId.Should().Be(KeyedTemplateId.PackageId);
        overGrpc.TemplateId.ModuleName.Should().Be(KeyedTemplateId.ModuleName);
        overGrpc.TemplateId.EntityName.Should().Be(KeyedTemplateId.EntityName);
        overRest.TemplateId.PackageId.Should().Be(KeyedTemplateId.PackageId);
        overRest.TemplateId.ModuleName.Should().Be(KeyedTemplateId.ModuleName);
        overRest.TemplateId.EntityName.Should().Be(KeyedTemplateId.EntityName);

        overGrpc.Choice.Should().Be("Transfer");
        overRest.Choice.Should().Be("Transfer");
    }

    [Fact]
    public void An_exercise_by_key_carries_the_same_key_and_choice_argument_on_both_transports()
    {
        var submission = Submission(ExerciseByKey(RecordKey()));

        var overGrpc = SingleByKeyOverGrpc(submission);
        var overRest = SingleByKeyOverRest(submission);

        var grpcKeyField = overGrpc.ContractKey.Record.Fields.Should().ContainSingle().Subject;
        var restKeyField = overRest.ContractKey.Record.Fields.Should().ContainSingle().Subject;
        grpcKeyField.Label.Should().Be("owner");
        grpcKeyField.Value.Party.Should().Be(Alice.Id);
        restKeyField.Label.Should().Be("owner");
        restKeyField.Value.Party.Should().Be(Alice.Id);

        var grpcArgumentField = overGrpc.ChoiceArgument.Record.Fields.Should().ContainSingle().Subject;
        var restArgumentField = overRest.ChoiceArgument.Record.Fields.Should().ContainSingle().Subject;
        grpcArgumentField.Label.Should().Be("newOwner");
        grpcArgumentField.Value.Party.Should().Be("party::bob");
        restArgumentField.Label.Should().Be("newOwner");
        restArgumentField.Value.Party.Should().Be("party::bob");
    }

    [Fact]
    public void A_bare_scalar_key_reaches_the_wire_as_the_scalar_itself_on_both_transports()
    {
        var submission = Submission(ExerciseByKey(ScalarKey()));

        var overGrpc = SingleByKeyOverGrpc(submission);
        var overRest = SingleByKeyOverRest(submission);

        overGrpc.ContractKey.Party.Should().Be("party::steward");
        overGrpc.ContractKey.Record.Should().BeNull();
        overRest.ContractKey.Party.Should().Be("party::steward");
        overRest.ContractKey.Record.Should().BeNull();
    }

    private static RuntimeCommands.CreateAndExerciseCommand CreateAndExercise() =>
        new(
            KeyedTemplateId,
            new DamlRecord(null, [new DamlField("owner", new DamlParty(Alice.Id))]),
            new RuntimeCommands.ChoiceName("Transfer"),
            new DamlRecord(null, [new DamlField("newOwner", new DamlParty("party::bob"))]));

    private static Com.Daml.Ledger.Api.V2.CreateAndExerciseCommand SingleCreateAndExerciseOverGrpc(
        RuntimeCommands.CommandsSubmission submission) =>
        OverGrpc(submission).Commands_.Should().ContainSingle().Subject.CreateAndExercise;

    private static Rest.Client.Raw.CreateAndExerciseCommand SingleCreateAndExerciseOverRest(
        RuntimeCommands.CommandsSubmission submission) =>
        OverRest(submission).Commands1.Should().ContainSingle().Subject.CreateAndExerciseCommand;

    [Fact]
    public void A_create_and_exercise_reaches_the_wire_as_the_create_and_exercise_arm_on_both_transports()
    {
        var submission = Submission(CreateAndExercise());

        var overGrpc = OverGrpc(submission).Commands_.Should().ContainSingle().Subject;
        var overRest = OverRest(submission).Commands1.Should().ContainSingle().Subject;

        overGrpc.CreateAndExercise.Should().NotBeNull();
        overGrpc.Exercise.Should().BeNull();
        overGrpc.ExerciseByKey.Should().BeNull();
        overGrpc.Create.Should().BeNull();
        overRest.CreateAndExerciseCommand.Should().NotBeNull();
        overRest.ExerciseCommand.Should().BeNull();
        overRest.ExerciseByKeyCommand.Should().BeNull();
        overRest.CreateCommand.Should().BeNull();
    }

    [Fact]
    public void A_create_and_exercise_carries_the_same_template_id_and_choice_name_on_both_transports()
    {
        var submission = Submission(CreateAndExercise());

        var overGrpc = SingleCreateAndExerciseOverGrpc(submission);
        var overRest = SingleCreateAndExerciseOverRest(submission);

        overGrpc.TemplateId.PackageId.Should().Be(KeyedTemplateId.PackageId);
        overGrpc.TemplateId.ModuleName.Should().Be(KeyedTemplateId.ModuleName);
        overGrpc.TemplateId.EntityName.Should().Be(KeyedTemplateId.EntityName);
        overRest.TemplateId.PackageId.Should().Be(KeyedTemplateId.PackageId);
        overRest.TemplateId.ModuleName.Should().Be(KeyedTemplateId.ModuleName);
        overRest.TemplateId.EntityName.Should().Be(KeyedTemplateId.EntityName);

        overGrpc.Choice.Should().Be("Transfer");
        overRest.Choice.Should().Be("Transfer");
    }

    [Fact]
    public void A_create_and_exercise_carries_the_same_create_arguments_and_choice_argument_on_both_transports()
    {
        var submission = Submission(CreateAndExercise());

        var overGrpc = SingleCreateAndExerciseOverGrpc(submission);
        var overRest = SingleCreateAndExerciseOverRest(submission);

        var grpcCreateArgumentField = overGrpc.CreateArguments.Fields.Should().ContainSingle().Subject;
        var restCreateArgumentField = overRest.CreateArguments.Fields.Should().ContainSingle().Subject;
        grpcCreateArgumentField.Label.Should().Be("owner");
        grpcCreateArgumentField.Value.Party.Should().Be(Alice.Id);
        restCreateArgumentField.Label.Should().Be("owner");
        restCreateArgumentField.Value.Party.Should().Be(Alice.Id);

        var grpcChoiceArgumentField = overGrpc.ChoiceArgument.Record.Fields.Should().ContainSingle().Subject;
        var restChoiceArgumentField = overRest.ChoiceArgument.Record.Fields.Should().ContainSingle().Subject;
        grpcChoiceArgumentField.Label.Should().Be("newOwner");
        grpcChoiceArgumentField.Value.Party.Should().Be("party::bob");
        restChoiceArgumentField.Label.Should().Be("newOwner");
        restChoiceArgumentField.Value.Party.Should().Be("party::bob");
    }
}
