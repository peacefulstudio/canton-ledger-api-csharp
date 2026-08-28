// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Abstractions.Tests;

public class ExerciseOutcomeExtensionsTests
{
    private static readonly IReadOnlyDictionary<string, string> SampleMetadata =
        new Dictionary<string, string> { ["participant"] = "participant1" };

    [Fact]
    public void OneOrThrow_returns_the_result_for_One()
    {
        ExerciseOutcome<int> outcome = new ExerciseOutcome<int>.One(42);

        outcome.OneOrThrow("Mint").Should().Be(42);
    }

    [Fact]
    public void OneOrThrow_throws_for_None_with_the_operation_in_message_and_Operation_property()
    {
        ExerciseOutcome<int> outcome = new ExerciseOutcome<int>.None();

        var act = () => outcome.OneOrThrow("Mint");

        var exception = act.Should().Throw<LedgerOperationException>().Which;
        exception.Message.Should().Contain("Mint").And.Contain("none");
        exception.Operation.Should().Be("Mint");
        exception.Category.Should().BeNull();
        exception.StatusCode.Should().BeNull();
    }

    [Fact]
    public void OneOrThrow_throws_for_Many_reporting_count_and_contract_ids()
    {
        ExerciseOutcome<int> outcome = new ExerciseOutcome<int>.Many(2, ["cid-1", "cid-2"]);

        var act = () => outcome.OneOrThrow("Mint");

        var exception = act.Should().Throw<LedgerOperationException>().Which;
        exception.Message.Should().Contain("Mint").And.Contain("2").And.Contain("cid-1").And.Contain("cid-2");
        exception.Operation.Should().Be("Mint");
    }

    [Fact]
    public void OneOrThrow_throws_for_DamlError_carrying_the_structured_payload()
    {
        ExerciseOutcome<int> outcome = new ExerciseOutcome<int>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "contract abc is not active",
            SampleMetadata);

        var act = () => outcome.OneOrThrow("Transfer");

        var exception = act.Should().Throw<LedgerOperationException>().Which;
        exception.Operation.Should().Be("Transfer");
        exception.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing);
        exception.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
        exception.Metadata.Should().BeSameAs(SampleMetadata);
        exception.Message.Should().Contain("Transfer").And.Contain("CONTRACT_NOT_FOUND")
            .And.Contain("contract abc is not active");
    }

    [Fact]
    public void OneOrThrow_throws_for_InfraError_carrying_status_code_and_source_exception()
    {
        var source = new TimeoutException("boom");
        ExerciseOutcome<int> outcome = new ExerciseOutcome<int>.InfraError(14, "participant unreachable", source);

        var act = () => outcome.OneOrThrow("Mint");

        var exception = act.Should().Throw<LedgerOperationException>().Which;
        exception.Operation.Should().Be("Mint");
        exception.StatusCode.Should().Be(14);
        exception.InnerException.Should().BeSameAs(source);
        exception.Message.Should().Contain("Mint").And.Contain("participant unreachable");
    }

    [Fact]
    public void OneOrThrow_rejects_a_null_outcome()
    {
        ExerciseOutcome<int> outcome = null!;

        var act = () => outcome.OneOrThrow("Mint");

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void OneOrThrow_rejects_a_missing_operation_name(string? operationName)
    {
        ExerciseOutcome<int> outcome = new ExerciseOutcome<int>.One(42);

        var act = () => outcome.OneOrThrow(operationName!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task OneOrThrowAsync_unwraps_the_awaited_outcome()
    {
        var outcomeTask = Task.FromResult<ExerciseOutcome<string>>(new ExerciseOutcome<string>.One("ok"));

        (await outcomeTask.OneOrThrowAsync("Mint")).Should().Be("ok");
    }

    [Fact]
    public async Task OneOrThrowAsync_throws_with_the_operation_context()
    {
        var outcomeTask = Task.FromResult<ExerciseOutcome<string>>(new ExerciseOutcome<string>.None());

        var act = () => outcomeTask.OneOrThrowAsync("Burn");

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.Operation.Should().Be("Burn");
    }

    [Fact]
    public async Task OneOrThrowAsync_rejects_a_null_outcome_task()
    {
        Task<ExerciseOutcome<int>> outcomeTask = null!;

        var act = () => outcomeTask.OneOrThrowAsync("Mint");

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OneOrThrowAsync_rejects_a_missing_operation_name(string? operationName)
    {
        var outcomeTask = Task.FromResult<ExerciseOutcome<int>>(new ExerciseOutcome<int>.One(42));

        var act = () => outcomeTask.OneOrThrowAsync(operationName!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task OneOrThrowAsync_rejects_a_missing_operation_name_before_awaiting_the_task()
    {
        var outcomeTask = Task.FromException<ExerciseOutcome<int>>(new InvalidOperationException("boom"));

        var act = () => outcomeTask.OneOrThrowAsync("   ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void ThrowIfError_returns_silently_for_One_None_and_Many()
    {
        var outcomes = new ExerciseOutcome<int>[]
        {
            new ExerciseOutcome<int>.One(1),
            new ExerciseOutcome<int>.None(),
            new ExerciseOutcome<int>.Many(2, ["cid-1", "cid-2"]),
        };

        foreach (var outcome in outcomes)
        {
            var act = () => outcome.ThrowIfError("Mint");

            act.Should().NotThrow();
        }
    }

    [Fact]
    public void ThrowIfError_throws_for_DamlError_with_operation_context()
    {
        ExerciseOutcome<int> outcome = new ExerciseOutcome<int>.DamlError(
            DamlErrorCategory.ContentionOnSharedResources,
            "LOCAL_VERDICT_LOCKED_CONTRACTS",
            "contended",
            SampleMetadata);

        var act = () => outcome.ThrowIfError("Settle");

        var exception = act.Should().Throw<LedgerOperationException>().Which;
        exception.Operation.Should().Be("Settle");
        exception.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
    }

    [Fact]
    public void ThrowIfError_throws_for_InfraError_with_operation_context()
    {
        ExerciseOutcome<int> outcome = new ExerciseOutcome<int>.InfraError(4, "deadline exceeded");

        var act = () => outcome.ThrowIfError("Settle");

        var exception = act.Should().Throw<LedgerOperationException>().Which;
        exception.Operation.Should().Be("Settle");
        exception.StatusCode.Should().Be(4);
    }

    [Fact]
    public void ThrowIfError_rejects_a_null_outcome()
    {
        ExerciseOutcome<int> outcome = null!;

        var act = () => outcome.ThrowIfError("Settle");

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ThrowIfError_rejects_a_missing_operation_name(string? operationName)
    {
        ExerciseOutcome<int> outcome = new ExerciseOutcome<int>.One(42);

        var act = () => outcome.ThrowIfError(operationName!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Operation_is_null_on_an_exception_thrown_without_operation_context()
    {
        var exception = new LedgerOperationException("no context");

        exception.Operation.Should().BeNull();
    }
}
