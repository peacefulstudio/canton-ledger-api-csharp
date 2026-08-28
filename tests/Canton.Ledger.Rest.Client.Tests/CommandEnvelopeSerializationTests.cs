// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class CommandEnvelopeSerializationTests
{
    [Fact]
    public void Command_serializes_the_create_arm_as_CreateCommand()
    {
        PropertyNamesOfSerialized(new Command { CreateCommand = new CreateCommand() })
            .Should().Equal("CreateCommand");
    }

    [Fact]
    public void Command_serializes_the_exercise_arm_as_ExerciseCommand()
    {
        PropertyNamesOfSerialized(new Command { ExerciseCommand = new ExerciseCommand() })
            .Should().Equal("ExerciseCommand");
    }

    [Fact]
    public void Command_serializes_the_exerciseByKey_arm_as_ExerciseByKeyCommand()
    {
        PropertyNamesOfSerialized(new Command { ExerciseByKeyCommand = new ExerciseByKeyCommand() })
            .Should().Equal("ExerciseByKeyCommand");
    }

    [Fact]
    public void Command_serializes_the_createAndExercise_arm_as_CreateAndExerciseCommand()
    {
        PropertyNamesOfSerialized(new Command { CreateAndExerciseCommand = new CreateAndExerciseCommand() })
            .Should().Equal("CreateAndExerciseCommand");
    }

    [Fact]
    public void Commands_nests_the_deduplication_offset_under_a_single_wrapper_key()
    {
        var commands = new Commands
        {
            DeduplicationPeriod = new DeduplicationPeriod { DeduplicationOffset = "42" },
        };

        var json = JsonSerializer.Serialize(commands, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        var period = document.RootElement.GetProperty("deduplicationPeriod");
        var keys = period.EnumerateObject().Select(property => property.Name).ToArray();
        keys.Should().Equal("DeduplicationOffset");
    }

    [Fact]
    public void Commands_nests_the_deduplication_duration_under_a_single_wrapper_key()
    {
        var commands = new Commands
        {
            DeduplicationPeriod = new DeduplicationPeriod { DeduplicationDuration = "30s" },
        };

        var json = JsonSerializer.Serialize(commands, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        var period = document.RootElement.GetProperty("deduplicationPeriod");
        var keys = period.EnumerateObject().Select(property => property.Name).ToArray();
        keys.Should().Equal("DeduplicationDuration");
    }

    [Fact]
    public void Commands_omits_the_deduplication_period_when_it_is_unset()
    {
        var commands = new Commands { CommandId = "no-dedup" };

        var json = JsonSerializer.Serialize(commands, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.TryGetProperty("deduplicationPeriod", out _).Should().BeFalse();
    }

    private static IEnumerable<string> PropertyNamesOfSerialized(Command command)
    {
        var json = JsonSerializer.Serialize(command, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
    }
}
