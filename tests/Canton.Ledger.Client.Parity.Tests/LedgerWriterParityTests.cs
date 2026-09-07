// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Richtypes;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Behavioral parity suite over <see cref="ILedgerWriter"/> and over the fire-and-forget
/// <see cref="ICantonLedgerClient.SubmitAsync"/> that sits beside it, run against every provider
/// that implements them (the in-memory Fake, REST, and gRPC) through one shared set of test bodies.
/// Each lane also provides the <see cref="Party"/> that owns the Marker contracts these bodies
/// create and exercise: the Fake stages a canned outcome regardless of payload, while REST and
/// gRPC allocate a real party and upload the shared <c>richtypes.dar</c> against a live LocalNet.
/// </summary>
public abstract class LedgerWriterParityTests
{
    /// <summary>
    /// Why this lane cannot read back the result of an exercise, or <see langword="null"/> when it
    /// can. Only the row that decodes a choice result skips with this reason: the create row reads
    /// back a contract id and the fire-and-forget row reads nothing back, so neither is held by it.
    /// </summary>
    protected virtual string? ExerciseResultDecodeQuarantine => null;

    /// <summary>
    /// Opens a lane over this provider's <see cref="ILedgerWriter"/> and its wider
    /// <see cref="ICantonLedgerClient"/> for one test.
    /// </summary>
    protected abstract Task<CapabilityLane<(ILedgerWriter Writer, ICantonLedgerClient Client, Party Owner)>> OpenWriterAsync(
        CancellationToken cancellationToken);

    [Fact]
    public async Task TryCreateAsync_creates_a_Marker_contract_and_returns_its_contract_id()
    {
        await using var lane = await OpenWriterAsync(TestContext.Current.CancellationToken);
        var (writer, _, owner) = lane.Capability;

        var outcome = await writer.TryCreateAsync(
            new Marker(owner), owner, cancellationToken: TestContext.Current.CancellationToken);

        var created = outcome.Should().BeOfType<ExerciseOutcome<ContractId<Marker>>.One>().Subject;
        created.Result.Value.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task TryExerciseAsync_exercises_the_Archive_choice_on_a_created_Marker_contract()
    {
        if (ExerciseResultDecodeQuarantine is { } quarantine)
        {
            Assert.Skip(quarantine);
        }

        await using var lane = await OpenWriterAsync(TestContext.Current.CancellationToken);
        var (writer, _, owner) = lane.Capability;
        var createOutcome = await writer.TryCreateAsync(
            new Marker(owner), owner, cancellationToken: TestContext.Current.CancellationToken);
        var markerCid = createOutcome.Should().BeOfType<ExerciseOutcome<ContractId<Marker>>.One>().Subject.Result;

        var command = ExerciseCommand.For(markerCid, Marker.ChoiceArchive.Name, DamlRecord.Create());
        var outcome = await writer.TryExerciseAsync<DamlUnit>(
            command, owner, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<DamlUnit>.One>();
    }

    [Fact]
    public async Task SubmitAsync_accepts_a_create_and_returns_the_command_id_the_submission_carried()
    {
        await using var lane = await OpenWriterAsync(TestContext.Current.CancellationToken);
        var (_, client, owner) = lane.Capability;
        var commandId = new CommandId(Guid.NewGuid().ToString());
        var submission = CommandsSubmission
            .Single(CreateCommand.For(new Marker(owner)))
            .WithActAs(owner)
            .WithCommandId(commandId);

        var effectiveCommandId = await client.SubmitAsync(
            submission, cancellationToken: TestContext.Current.CancellationToken);

        effectiveCommandId.Should().Be(commandId);
    }
}
