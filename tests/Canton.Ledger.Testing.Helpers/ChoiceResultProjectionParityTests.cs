// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// Behavioural parity suite over the choice-result projection, run against every transport's
/// transaction-result projector through one shared set of test bodies. It pins the decisions both
/// transports must agree on: that a choice return which legitimately decodes to <c>null</c> — an
/// <c>Optional</c>- or unit-shaped return — is reported as
/// <see cref="ExerciseOutcome{T}.None"/> rather than a <see cref="ExerciseOutcome{T}.One"/>
/// carrying <c>null</c> in a non-nullable slot, that a decoded return is reported as
/// <see cref="ExerciseOutcome{T}.One"/>, that a failed submission keeps its error arm, and that a
/// committed transaction the projection cannot read a single typed return from is reported as
/// <see cref="ExerciseOutcome{T}.CommittedUndecodable"/> carrying its update id, never thrown. The
/// wire decoding that produces the <see cref="TransactionResult"/> stays per-transport.
/// </summary>
public abstract class ChoiceResultProjectionParityTests
{
    /// <summary>A choice whose return decodes to <c>null</c> for the projected result type.</summary>
    protected static readonly ChoiceName NullDecodingChoice = new("Acknowledge");

    /// <summary>A choice whose return decodes to a value for the projected result type.</summary>
    protected static readonly ChoiceName PartyReturningChoice = new("GetOwner");

    /// <summary>The party a successful <see cref="PartyReturningChoice"/> exercise returns.</summary>
    protected const string ReturnedParty = "alice::ns1";

    /// <summary>
    /// Projects <paramref name="outcome"/> into this transport's typed choice-result outcome for
    /// <paramref name="choice"/>.
    /// </summary>
    protected abstract ExerciseOutcome<TResult> ProjectChoiceResult<TResult>(
        ExerciseOutcome<TransactionResult> outcome, ChoiceName choice);

    [Fact]
    public void ProjectChoiceResult_reports_a_choice_return_that_decodes_to_null_as_None()
    {
        var projected = ProjectChoiceResult<DamlRecord>(
            Exercised(NullDecodingChoice, DamlUnit.Instance), NullDecodingChoice);

        projected.Should().BeOfType<ExerciseOutcome<DamlRecord>.None>();
    }

    [Fact]
    public void ProjectChoiceResult_reports_a_choice_return_that_decodes_to_a_value_as_One()
    {
        var projected = ProjectChoiceResult<Party>(
            Exercised(PartyReturningChoice, new DamlParty(ReturnedParty)), PartyReturningChoice);

        projected.Should().BeOfType<ExerciseOutcome<Party>.One>()
            .Subject.Result.Should().Be((Party)ReturnedParty);
    }

    [Fact]
    public void ProjectChoiceResult_passes_a_DamlError_outcome_through()
    {
        var projected = ProjectChoiceResult<DamlRecord>(
            new ExerciseOutcome<TransactionResult>.DamlError(
                DamlErrorCategory.InvalidGivenCurrentSystemStateOther,
                "SOME_ERROR",
                "boom",
                new Dictionary<string, string>()),
            NullDecodingChoice);

        projected.Should().BeOfType<ExerciseOutcome<DamlRecord>.DamlError>()
            .Subject.ErrorId.Should().Be("SOME_ERROR");
    }

    [Fact]
    public void ProjectChoiceResult_passes_an_InfraError_outcome_through()
    {
        var projected = ProjectChoiceResult<DamlRecord>(
            new ExerciseOutcome<TransactionResult>.InfraError(503, "unavailable"),
            NullDecodingChoice);

        projected.Should().BeOfType<ExerciseOutcome<DamlRecord>.InfraError>()
            .Subject.StatusCode.Should().Be(503);
    }

    [Fact]
    public void ProjectChoiceResult_passes_a_CommittedUndecodable_outcome_through()
    {
        var sourceException = new FormatException("Cannot parse wire Int64 value 'x' as a 64-bit integer.");

        var projected = ProjectChoiceResult<DamlRecord>(
            new ExerciseOutcome<TransactionResult>.CommittedUndecodable("upd-1", "cannot decode", sourceException),
            NullDecodingChoice);

        var undecodable = projected.Should().BeOfType<ExerciseOutcome<DamlRecord>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().Be("upd-1");
        undecodable.Message.Should().Be("cannot decode");
        undecodable.SourceException.Should().BeSameAs(sourceException);
    }

    [Fact]
    public void ProjectChoiceResult_reports_a_committed_transaction_without_the_choice_as_CommittedUndecodable()
    {
        var projected = ProjectChoiceResult<Party>(
            Exercised(NullDecodingChoice, DamlUnit.Instance), PartyReturningChoice);

        var undecodable = projected.Should().BeOfType<ExerciseOutcome<Party>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().Be("upd-1");
        undecodable.Message.Should().Be(
            "The command committed, but its choice result could not be read: Transaction contains no exercised event for choice 'GetOwner'.");
        undecodable.SourceException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public void ProjectChoiceResult_reports_a_committed_transaction_exercising_the_choice_twice_as_CommittedUndecodable()
    {
        var projected = ProjectChoiceResult<Party>(
            Exercised(
                (PartyReturningChoice, new DamlParty(ReturnedParty)),
                (PartyReturningChoice, new DamlParty(ReturnedParty))),
            PartyReturningChoice);

        var undecodable = projected.Should().BeOfType<ExerciseOutcome<Party>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().Be("upd-1");
        undecodable.Message.Should().Be(
            "The command committed, but its choice result could not be read: Transaction contains 2 exercised events for choice 'GetOwner', expected exactly 1.");
        undecodable.SourceException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public void ProjectChoiceResult_reports_a_result_type_without_a_Daml_mapping_as_CommittedUndecodable()
    {
        var projected = ProjectChoiceResult<ResultTypeWithoutDamlMapping>(
            Exercised(PartyReturningChoice, new DamlParty(ReturnedParty)), PartyReturningChoice);

        var undecodable = projected
            .Should().BeOfType<ExerciseOutcome<ResultTypeWithoutDamlMapping>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().Be("upd-1");
        undecodable.Message.Should().StartWith("The command committed, but its choice result could not be read: ");
        undecodable.SourceException.Should().BeOfType<NotSupportedException>();
    }

    private static ExerciseOutcome<TransactionResult>.One Exercised(ChoiceName choice, DamlValue exerciseResult) =>
        Exercised((choice, exerciseResult));

    private static ExerciseOutcome<TransactionResult>.One Exercised(
        params (ChoiceName Choice, DamlValue ExerciseResult)[] exercises) =>
        new(new TransactionResult(
            "upd-1", LedgerOffset.At(1), [], [], new CommandId("cmd-1"))
        {
            ExercisedEvents = EquatableArray.Create(exercises.Select(exercise => new ExercisedEvent(
                "00holding",
                new RuntimeIdentifier("tmpl-pkg", "Sample.Token", "Holding"),
                null,
                exercise.Choice.Value,
                DamlUnit.Instance,
                exercise.ExerciseResult,
                false,
                [(Party)ReturnedParty],
                [(Party)ReturnedParty]))),
        });

    /// <summary>A result type <c>FromDamlValue</c> has no mapping for.</summary>
    public sealed class ResultTypeWithoutDamlMapping;
}
