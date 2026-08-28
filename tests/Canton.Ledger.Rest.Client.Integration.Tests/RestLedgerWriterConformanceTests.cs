// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Peaceful.Canton.Localnet.Testing;
using Richtypes;
using Xunit;

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// LocalNet conformance coverage for <see cref="RestLedgerClient"/>'s <see cref="ILedgerWriter"/>
/// surface: <see cref="ILedgerWriter.TryCreateAsync{TTemplate}"/>,
/// <see cref="ILedgerWriter.TryExerciseAsync{TResult}"/>,
/// <see cref="ILedgerWriter.SubmitAndWaitAsync"/>, and
/// <see cref="ILedgerWriter.TrySubmitAndWaitForTransactionAsync"/>, each exercised against a real
/// participant using the shared <c>richtypes.dar</c> Marker template (create + consuming Archive).
/// </summary>
[Trait("Category", "Integration")]
public class RestLedgerWriterConformanceTests
{
    private const string ExerciseResultQuarantineMessage =
        "Quarantined on the exercise response, not on the submission: the participant accepts the "
        + "exercise and commits it. One measured cause keeps its result unreadable. The client now "
        + "requests the ledger-effects transaction shape on submit-and-wait-for-transaction, so the "
        + "transaction does carry the ExercisedEvent, but its choiceArgument and exerciseResult both "
        + "arrive as {}, an untyped wire Value with no sum case set, which the decoder cannot "
        + "resolve without knowing the Daml type the value belongs to. The submitted bytes are "
        + "covered live by "
        + nameof(TryCreateAsync_submits_a_create_the_participant_accepts) + ". The quarantine "
        + "lifts when the decode fix lands; until then the bytes this test would submit and both "
        + "measured participant replies are pinned by RestExerciseQuarantinePinTests in "
        + "Canton.Ledger.Rest.Client.Tests.";

    private static string DarPath() => Path.Combine(
        AppContext.BaseDirectory, "testdata", "richtypes", "richtypes.dar");

    private static async Task<Party> NewOwnerAsync(RestConformanceLane lane, CancellationToken cancellationToken)
    {
        var darOutcome = await lane.Fixture.UploadDarAsync(DarPath(), cancellationToken);
        Assert.True(
            darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
            $"Unexpected DAR upload outcome: {darOutcome}");

        var party = await lane.Fixture.AllocatePartyAsync("rest-writer", cancellationToken: cancellationToken);
        await lane.Fixture.GrantUserRightsAsync(
            lane.Fixture.ValidatorUserId, actAs: [party.PartyId], cancellationToken: cancellationToken);
        return new Party(party.PartyId);
    }

    [Fact]
    public async Task TryCreateAsync_submits_a_create_the_participant_accepts()
    {
        var recorder = new RecordingRequestHandler();
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken, recorder);
        var owner = await NewOwnerAsync(lane, TestContext.Current.CancellationToken);
        recorder.Bodies.Clear();

        var outcome = await lane.LedgerClient.TryCreateAsync(
            new Marker(owner), owner, cancellationToken: TestContext.Current.CancellationToken);

        recorder.Bodies.Should().ContainSingle();
        recorder.Bodies.TryDequeue(out var submitted).Should().BeTrue();
        AssertSubmittedCreateCommand(submitted!, owner);
        AssertBodyWasNotRejected(outcome);
    }

    [Fact]
    public async Task TryCreateAsync_creates_a_Marker_contract_on_a_real_participant()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);
        var owner = await NewOwnerAsync(lane, TestContext.Current.CancellationToken);

        var outcome = await lane.LedgerClient.TryCreateAsync(
            new Marker(owner), owner, cancellationToken: TestContext.Current.CancellationToken);

        var created = Assert.IsType<ExerciseOutcome<ContractId<Marker>>.One>(outcome);
        created.Result.Value.Should().NotBeNullOrWhiteSpace();
    }

    private static void AssertBodyWasNotRejected(ExerciseOutcome<ContractId<Marker>> outcome)
    {
        var rejection = outcome as ExerciseOutcome<ContractId<Marker>>.InfraError;

        rejection.Should().BeNull(
            "the participant must accept the submitted bytes and its reply must decode, but it answered {0} {1}",
            rejection?.StatusCode,
            rejection?.Message);
    }

    private static void AssertSubmittedCreateCommand(string submitted, Party owner)
    {
        using var document = JsonDocument.Parse(submitted);
        var envelope = document.RootElement.GetProperty("commands");

        envelope.GetProperty("actAs").EnumerateArray().Select(actor => actor.GetString())
            .Should().Equal(owner.Id);

        var command = envelope.GetProperty("commands").EnumerateArray().Should().ContainSingle().Subject;
        command.EnumerateObject().Select(arm => arm.Name).Should().Equal("CreateCommand");

        var create = command.GetProperty("CreateCommand");
        create.GetProperty("templateId").GetString()
            .Should().Be($"{Marker.PackageId}:RichTypes:Marker");
        create.GetProperty("createArguments").GetProperty("owner").GetString()
            .Should().Be(owner.Id);
    }

    [Fact(Skip = ExerciseResultQuarantineMessage)]
    public async Task TryExerciseAsync_exercises_the_Archive_choice_on_a_real_participant()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);
        var owner = await NewOwnerAsync(lane, TestContext.Current.CancellationToken);

        var createOutcome = await lane.LedgerClient.TryCreateAsync(
            new Marker(owner), owner, cancellationToken: TestContext.Current.CancellationToken);
        var markerCid = Assert.IsType<ExerciseOutcome<ContractId<Marker>>.One>(createOutcome).Result;

        var exerciseCommand = ExerciseCommand.For(markerCid, Marker.ChoiceArchive.Name, DamlRecord.Create());
        var exerciseOutcome = await lane.LedgerClient.TryExerciseAsync<DamlUnit>(
            exerciseCommand, owner, cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<ExerciseOutcome<DamlUnit>.One>(exerciseOutcome);
    }

    [Fact]
    public async Task SubmitAndWaitAsync_submits_a_create_command_and_returns_an_update_id_and_offset()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);
        var owner = await NewOwnerAsync(lane, TestContext.Current.CancellationToken);
        var submission = CommandsSubmission.Single(CreateCommand.For(new Marker(owner)), owner);

        var result = await lane.LedgerClient.SubmitAndWaitAsync(
            submission, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(result.UpdateId), "returned update id is empty");
        Assert.True(result.CompletionOffset.Value > 0, "returned completion offset must be positive");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_submits_a_create_command_and_returns_the_created_contract()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);
        var owner = await NewOwnerAsync(lane, TestContext.Current.CancellationToken);
        var submission = CommandsSubmission.Single(CreateCommand.For(new Marker(owner)), owner);

        var outcome = await lane.LedgerClient.TrySubmitAndWaitForTransactionAsync(
            submission, cancellationToken: TestContext.Current.CancellationToken);

        var success = Assert.IsType<ExerciseOutcome<TransactionResult>.One>(outcome);
        Assert.Contains(
            success.Result.CreatedContracts,
            c => c.TemplateId.EntityName == "Marker");
    }
}
