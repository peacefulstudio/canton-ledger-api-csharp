// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Xunit;
using WireCompletion = Canton.Ledger.Rest.Client.Raw.Completion;
using WireDeduplicationPeriod = Canton.Ledger.Rest.Client.Raw.DeduplicationPeriod;
using WireStatus = Canton.Ledger.Rest.Client.Raw.Status;
using WireSynchronizerTime = Canton.Ledger.Rest.Client.Raw.SynchronizerTime;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestCompletionProjectorTests
{
    [Fact]
    public void Project_maps_a_zero_status_completion_to_CommandAccepted_carrying_the_update_id_and_neutral_fields()
    {
        var completion = new WireCompletion
        {
            CommandId = "cmd-1",
            UpdateId = "update-1",
            Offset = "42",
            Status = new WireStatus { Code = 0 },
        };

        var result = RestCompletionProjector.Project(completion);

        var accepted = result.Should().BeOfType<CompletionStreamEvent.CommandAccepted>().Subject;
        accepted.UpdateId.Should().Be("update-1");
        accepted.Completion.CommandId.Value.Should().Be("cmd-1");
        accepted.Completion.Offset.Should().Be(42L);
    }

    [Fact]
    public void Project_maps_a_completion_with_no_status_set_to_CommandAccepted()
    {
        var completion = new WireCompletion
        {
            CommandId = "cmd-1",
            UpdateId = "update-1",
            Offset = "1",
        };

        var result = RestCompletionProjector.Project(completion);

        var accepted = result.Should().BeOfType<CompletionStreamEvent.CommandAccepted>().Subject;
        accepted.UpdateId.Should().Be("update-1");
        accepted.Completion.CommandId.Value.Should().Be("cmd-1");
    }

    [Fact]
    public void Project_maps_a_non_zero_status_completion_to_CommandRejected_carrying_the_completion_status()
    {
        var completion = new WireCompletion
        {
            CommandId = "cmd-2",
            Offset = "7",
            Status = new WireStatus { Code = 3, Message = "INVALID_ARGUMENT" },
        };

        var result = RestCompletionProjector.Project(completion);

        var rejected = result.Should().BeOfType<CompletionStreamEvent.CommandRejected>().Subject;
        rejected.Status.Code.Should().Be(3);
        rejected.Status.Message.Should().Be("INVALID_ARGUMENT");
        rejected.Completion.CommandId.Value.Should().Be("cmd-2");
    }

    public static TheoryData<WireCompletion, long?, TimeSpan?> DeduplicationPeriods => new()
    {
        {
            new WireCompletion
            {
                CommandId = "d-offset",
                Offset = "1",
                DeduplicationPeriod = new WireDeduplicationPeriod { DeduplicationOffset = "100" },
            },
            100L, null
        },
        {
            new WireCompletion
            {
                CommandId = "d-duration",
                Offset = "1",
                DeduplicationPeriod = new WireDeduplicationPeriod { DeduplicationDuration = "30s" },
            },
            null, TimeSpan.FromSeconds(30)
        },
        { new WireCompletion { CommandId = "d-none", Offset = "1" }, null, null },
    };

    [Theory]
    [MemberData(nameof(DeduplicationPeriods))]
    public void Project_maps_the_deduplication_period_to_the_matching_nullable(
        WireCompletion completion, long? expectedOffset, TimeSpan? expectedDuration)
    {
        var result = RestCompletionProjector.Project(completion);

        var payload = result.Should().BeOfType<CompletionStreamEvent.CommandAccepted>().Subject.Completion;
        payload.DeduplicationOffset.Should().Be(expectedOffset);
        payload.DeduplicationDuration.Should().Be(expectedDuration);
    }

    [Fact]
    public void Project_maps_act_as_parties_synchronizer_time_and_the_optional_ids()
    {
        var recordTime = new DateTimeOffset(2026, 7, 20, 10, 30, 0, TimeSpan.Zero);
        var completion = new WireCompletion
        {
            CommandId = "cmd-4",
            Offset = "1",
            SubmissionId = "sub-4",
            UserId = "user-4",
            ActAs = ["alice", "bob"],
            SynchronizerTime = new WireSynchronizerTime
            {
                SynchronizerId = "sync-1",
                RecordTime = recordTime,
            },
        };

        var payload = RestCompletionProjector.Project(completion)
            .Should().BeOfType<CompletionStreamEvent.CommandAccepted>().Subject.Completion;

        payload.CommandId.Value.Should().Be("cmd-4");
        payload.ActAs.Select(p => p.Id).Should().Equal("alice", "bob");
        payload.SynchronizerTime.SynchronizerId.Should().Be("sync-1");
        payload.SynchronizerTime.RecordTime.Should().Be(recordTime);
        payload.SubmissionId.Should().Be("sub-4");
        payload.UserId.Should().Be("user-4");
    }

    [Fact]
    public void Project_throws_ArgumentNullException_when_completion_is_null()
    {
        var act = () => RestCompletionProjector.Project(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("completion");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Project_throws_a_malformed_response_when_an_accepted_completion_carries_no_command_id(string? commandId)
    {
        var completion = new WireCompletion
        {
            CommandId = commandId!,
            UpdateId = "update-1",
            Offset = "42",
            Status = new WireStatus { Code = 0 },
        };

        var act = () => RestCompletionProjector.Project(completion);

        act.Should().Throw<InvalidOperationException>(
                "the Ledger API marks Completion.commandId required, so an absent one is a malformed response "
                + "rather than a completion carrying a command id that cannot be read")
            .WithMessage("Malformed response from ledger*commandId*")
            .WithMessage("*offset '42'*");
    }

    [Fact]
    public void Project_throws_a_malformed_response_when_a_rejected_completion_carries_no_command_id()
    {
        var completion = new WireCompletion
        {
            Offset = "7",
            Status = new WireStatus { Code = 3, Message = "INVALID_ARGUMENT" },
        };

        var act = () => RestCompletionProjector.Project(completion);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Malformed response from ledger*commandId*");
    }

    [Fact]
    public void Project_raises_the_command_id_failure_in_the_shared_malformed_response_shape()
    {
        var completion = new WireCompletion { UpdateId = "update-1", Offset = "42" };

        var thrown = Record.Exception(() => RestCompletionProjector.Project(completion));

        RestTransactionResultProjector.IsMalformedResponse(thrown!).Should().BeTrue(
            "the REST client classifies a malformed wire body by this prefix, and a completion missing a "
            + "required field must land in that class rather than as an unrelated InvalidOperationException");
    }

    [Fact]
    public void Project_leaves_a_present_command_id_readable_on_the_projected_completion()
    {
        var completion = new WireCompletion { CommandId = "cmd-present", UpdateId = "update-1", Offset = "42" };

        var payload = RestCompletionProjector.Project(completion)
            .Should().BeOfType<CompletionStreamEvent.CommandAccepted>().Subject.Completion;

        var readCommandId = () => payload.CommandId.Value;
        readCommandId.Should().NotThrow().Which.Should().Be("cmd-present");
    }
}
