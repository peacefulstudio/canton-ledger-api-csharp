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
/// <see cref="ExerciseOutcome{T}.One"/>, and that a failed submission keeps its error arm. The
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

    private static ExerciseOutcome<TransactionResult>.One Exercised(ChoiceName choice, DamlValue exerciseResult) =>
        new(new TransactionResult(
            "upd-1", LedgerOffset.At(1), [], [], new CommandId("cmd-1"))
        {
            ExercisedEvents =
            [
                new ExercisedEvent(
                    "00holding",
                    new RuntimeIdentifier("tmpl-pkg", "Sample.Token", "Holding"),
                    null,
                    choice.Value,
                    DamlUnit.Instance,
                    exerciseResult,
                    false,
                    [(Party)ReturnedParty],
                    [(Party)ReturnedParty]),
            ],
        });
}
