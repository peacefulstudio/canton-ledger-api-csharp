// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using FluentAssertions;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class TransactionResultExerciseExtensionsTests
{
    [Fact]
    public void ExerciseResult_returns_typed_value_for_single_match()
    {
        var result = MakeResult(("GetTrailingTwap", new DamlNumeric(42.5m)));

        result.ExerciseResult<decimal>("GetTrailingTwap").Should().Be(42.5m);
    }

    [Fact]
    public void ExerciseResult_returns_DamlRecord_instance_for_record_result()
    {
        var record = DamlRecord.Create(
            new Identifier("pkg", "Report", "Report"),
            DamlField.Create("total", new DamlNumeric(7m)));
        var result = MakeResult(("ComputeReport", record));

        result.ExerciseResult<DamlRecord>("ComputeReport").Should().BeSameAs(record);
    }

    [Fact]
    public void ExerciseResult_matches_choice_name_case_sensitively()
    {
        var result = MakeResult(("GetTrailingTwap", new DamlNumeric(1m)));

        var act = () => result.ExerciseResult<decimal>("gettrailingtwap");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no exercised event for choice 'gettrailingtwap'*");
    }

    [Fact]
    public void ExerciseResult_propagates_NotSupportedException_for_unsupported_TReturn()
    {
        var result = MakeResult(("GetTrailingTwap", new DamlNumeric(1m)));

        var act = () => result.ExerciseResult<Uri>("GetTrailingTwap");

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void ExerciseResult_throws_when_result_is_null()
    {
        var act = () => ((TransactionResult)null!).ExerciseResult<decimal>("GetTrailingTwap");

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ExerciseResult_throws_when_choiceName_is_null_or_empty(string? choiceName)
    {
        var result = MakeResult(("GetTrailingTwap", new DamlNumeric(1m)));

        var act = () => result.ExerciseResult<decimal>(choiceName!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AllExerciseResults_throws_when_result_is_null()
    {
        var act = () => ((TransactionResult)null!).AllExerciseResults<decimal>("GetTrailingTwap");

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AllExerciseResults_throws_when_choiceName_is_null_or_empty(string? choiceName)
    {
        var result = MakeResult(("GetTrailingTwap", new DamlNumeric(1m)));

        var act = () => result.AllExerciseResults<decimal>(choiceName!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ExerciseResult_throws_when_no_matching_choice()
    {
        var result = MakeResult(("Other", new DamlNumeric(1m)));

        var act = () => result.ExerciseResult<decimal>("GetTrailingTwap");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no exercised event for choice 'GetTrailingTwap'*");
    }

    [Fact]
    public void ExerciseResult_throws_when_multiple_matching_choices()
    {
        var result = MakeResult(
            ("GetTrailingTwap", new DamlNumeric(1m)),
            ("GetTrailingTwap", new DamlNumeric(2m)));

        var act = () => result.ExerciseResult<decimal>("GetTrailingTwap");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*2 exercised events for choice 'GetTrailingTwap'*expected exactly 1*");
    }

    [Fact]
    public void AllExerciseResults_returns_typed_values_in_transaction_order()
    {
        var result = MakeResult(
            ("GetTrailingTwap", new DamlNumeric(1m)),
            ("Other", new DamlNumeric(99m)),
            ("GetTrailingTwap", new DamlNumeric(2m)));

        result.AllExerciseResults<decimal>("GetTrailingTwap").Should().Equal(1m, 2m);
    }

    [Fact]
    public void AllExerciseResults_returns_empty_when_no_matching_choice()
    {
        var result = MakeResult(("Other", new DamlNumeric(1m)));

        result.AllExerciseResults<decimal>("GetTrailingTwap").Should().BeEmpty();
    }

    [Fact]
    public void AllExerciseResults_propagates_NotSupportedException_for_unsupported_TReturn()
    {
        var result = MakeResult(("GetTrailingTwap", new DamlNumeric(1m)));

        var act = () => result.AllExerciseResults<Uri>("GetTrailingTwap");

        act.Should().Throw<NotSupportedException>();
    }

    private static TransactionResult MakeResult(params (string ChoiceName, DamlValue Result)[] exercised)
    {
        var templateId = new Identifier("pkg", "Module", "Template");
        var events = new List<ExercisedEvent>();
        foreach (var (choiceName, choiceResult) in exercised)
        {
            events.Add(new ExercisedEvent(
                ContractId: "00cid",
                TemplateId: templateId,
                InterfaceId: null,
                ChoiceName: choiceName,
                ChoiceArgument: DamlUnit.Instance,
                ExerciseResult: choiceResult,
                Consuming: false,
                ActingParties: [(Party)"alice"],
                WitnessParties: [(Party)"alice"]));
        }

        return new TransactionResult(
            UpdateId: "u1",
            CompletionOffset: 1L,
            CreatedContracts: [],
            ArchivedContractIds: [])
        {
            ExercisedEvents = events,
        };
    }
}
