// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class ExerciseOutcomeArmCoverageTests
{
    private static readonly string[] RecordedArms =
    [
        nameof(ExerciseOutcome<object>.DamlError),
        nameof(ExerciseOutcome<object>.InfraError),
        nameof(ExerciseOutcome<object>.Many),
        nameof(ExerciseOutcome<object>.None),
        nameof(ExerciseOutcome<object>.One),
    ];

    [Fact]
    public void ExerciseOutcome_arms_are_all_named_by_the_submission_span_recorder()
    {
        var arms = typeof(ExerciseOutcome<>)
            .GetNestedTypes()
            .Where(nested => nested.IsSubclassOf(typeof(ExerciseOutcome<>)) || nested.BaseType?.Name == typeof(ExerciseOutcome<>).Name)
            .Select(nested => nested.Name)
            .Order()
            .ToArray();

        arms.Should().BeEquivalentTo(
            RecordedArms,
            "SubmissionClient.RecordOutcome switches over every arm by name, so a repin that adds one "
            + "must decide whether it belongs on the span rather than falling through unrecorded");
    }
}
