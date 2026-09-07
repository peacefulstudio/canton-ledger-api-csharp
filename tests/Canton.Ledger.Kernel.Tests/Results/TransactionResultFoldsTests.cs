// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Results;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Results;

public class TransactionResultFoldsTests
{
    private static readonly Identifier TemplateId = TemplateMarker.TemplateId;

    [Fact]
    public void ToCreatedContractId_reports_None_when_nothing_matches_the_marker()
    {
        var outcome = TransactionResultFolds.ToCreatedContractId<TemplateMarker>(
            Result(Created("00a"), Created("00b")), _ => false);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<TemplateMarker>>.None>();
    }

    [Fact]
    public void ToCreatedContractId_reports_the_single_match_as_a_typed_contract_id()
    {
        var outcome = TransactionResultFolds.ToCreatedContractId<TemplateMarker>(
            Result(Created("00a"), Created("00b")), created => created.ContractId == "00b");

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<TemplateMarker>>.One>()
            .Which.Result.Should().Be(new ContractId<TemplateMarker>("00b"));
    }

    [Fact]
    public void ToCreatedContractId_reports_every_match_and_its_count_when_more_than_one_matches()
    {
        var outcome = TransactionResultFolds.ToCreatedContractId<TemplateMarker>(
            Result(Created("00a"), Created("00b"), Created("00c")), _ => true);

        var many = outcome.Should().BeOfType<ExerciseOutcome<ContractId<TemplateMarker>>.Many>().Subject;
        many.Count.Should().Be(3);
        many.ContractIds.Should().Equal("00a", "00b", "00c");
    }

    [Fact]
    public void Project_hands_a_successful_outcome_to_the_success_projection()
    {
        var projected = TransactionResultFolds.Project(
            new ExerciseOutcome<TransactionResult>.One(Result(Created("00a"))),
            result => new ExerciseOutcome<int>.One(result.CreatedContracts.Count));

        projected.Should().BeOfType<ExerciseOutcome<int>.One>().Which.Result.Should().Be(1);
    }

    [Fact]
    public void Project_carries_a_DamlError_across_unchanged()
    {
        var metadata = new Dictionary<string, string> { ["participant"] = "p1" };

        var projected = TransactionResultFolds.Project(
            new ExerciseOutcome<TransactionResult>.DamlError(
                DamlErrorCategory.InvalidGivenCurrentSystemStateOther, "SOME_ERROR", "gone", metadata),
            _ => new ExerciseOutcome<int>.One(0));

        var damlError = projected.Should().BeOfType<ExerciseOutcome<int>.DamlError>().Subject;
        damlError.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateOther);
        damlError.ErrorId.Should().Be("SOME_ERROR");
        damlError.Message.Should().Be("gone");
        damlError.Metadata.Should().BeSameAs(metadata);
    }

    [Fact]
    public void Project_carries_an_InfraError_across_unchanged()
    {
        var sourceException = new InvalidOperationException("transport down");

        var projected = TransactionResultFolds.Project(
            new ExerciseOutcome<TransactionResult>.InfraError(503, "participant unavailable", SourceException: sourceException),
            _ => new ExerciseOutcome<int>.One(0));

        var infraError = projected.Should().BeOfType<ExerciseOutcome<int>.InfraError>().Subject;
        infraError.StatusCode.Should().Be(503);
        infraError.Message.Should().Be("participant unavailable");
        infraError.SourceException.Should().BeSameAs(sourceException);
    }

    [Fact]
    public void Project_rejects_a_null_outcome()
    {
        var act = () => TransactionResultFolds.Project<int>(null!, _ => new ExerciseOutcome<int>.One(0));

        act.Should().Throw<ArgumentNullException>();
    }

    private static CreatedContract Created(string contractId) =>
        new("0", contractId, TemplateId, DamlRecord.Create(), [], [], [], ContractKey: null);

    private static TransactionResult Result(params CreatedContract[] created) =>
        new(
            UpdateId: "u1",
            CompletionOffset: LedgerOffset.At(1),
            CreatedContracts: created,
            ArchivedContractIds: [],
            CommandId: default);
}
